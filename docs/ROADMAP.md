# OmniAgent Console — Yol Haritası ve Karar Kayıtları

> Kaynak: 2026-07-20 kod incelemesi + uygulanan düzeltmeler sonrası kalan kalemlerin uygulama planı.
> Durum anı: 79/79 unit test yeşil; baseline commit `4fd390c`; infra portları loopback; cross-process cancel Redis üzerinden canlı doğrulanmış.

## 0. Durum özeti

| # | Bulgu | Durum | Not |
|---|-------|-------|-----|
| 1 | Cross-process cancel | ✅ Kapandı | Redis `task-cancellations` + DB-before-token + tool-loop DB check |
| 2 | API restart recovery race | ✅ Kapandı | Recovery yalnız single-process (RabbitMQ yok) |
| 3 | Poison message infinite requeue | ✅ Kapandı | Redelivered → 2. fail = Failed+ACK |
| 4a | Açık infra portları | ✅ Kapandı | `INFRA_BIND_ADDRESS=127.0.0.1` |
| 4b | Credential at-rest (Vault secret-ref) | ✅ Kapandı (2026-07-20) | §4 — Vault path + dual-read migrate + metadata'da raw key yok |
| 5 | Tenant / sınıf izolasyonu | ✅ Kapandı (2026-07-20) | §1 — dual deployment uygulandı: laptop default, shared-lab `SHARED_LAB=true` ile; iki-path canlı doğrulama yapıldı |
| 6 | InputSanitizer darlığı | ✅ Kapandı (kısmi, doğası gereği) | Genişletildi; pattern-dışı secret teorik risk |
| 7 | Orchestrator god-class | ✅ Kapandı (2026-07-20) | §2 — R1–R4 dilimleri uygulandı; 1442 → 487 satır, davranış değişmedi |
| 8 | Reasoning-only boş content | ✅ Kapandı | `reasoning_content`/`reasoning` fallback |
| 9 | Frontend test yok | ✅ Kapandı (2026-07-20) | §3 — Vitest + 27 frontend unit test |
| 10 | İlk commit | ✅ Kapandı | `4fd390c` |
| 11 | Docs drift (agent.md) | ✅ Kapandı | §14–15 historical, §21 eklendi |
| — | Reviewer→Coder fix loop | ⭐ Opsiyonel | §5.1 |
| — | OpenSearch entegrasyonu | 🔮 Future | Profil arkasında, log ship yok |

**Önerilen öncelik**: ~~A1–E tamam~~. Yeni işler ihtiyaç oldukça ROADMAP'e eklenir.

---

## 1. Tenant / sınıf izolasyonu — KARAR: dual deployment profili

**Karar (2026-07-20)**: "Laptop-only mu, shared-server mı?" sorusu **ikisi birden** diye kapandı. Bu tek "ya o ya bu" ürün değil; aynı kod tabanında ortam değişkeniyle seçilen iki deployment profili:

| Profil | Ne zaman | İzolasyon | Gereken |
|--------|----------|-----------|---------|
| **Laptop-only** (default) | Her öğrenci kendi makinesinde `compose up` | Gerekmez (tek kullanıcı) | Hiçbir şey — bugünkü davranış |
| **Shared-lab** (opt-in) | Tek host, sınıf aynı URL'e girer | Session + ownership + workspace prefix | `SHARED_LAB=true` + `CONSOLE_API_KEY` |

Ürün cevabı "ikisi de olsun"; mühendislik cevabı "default tek-kullanıcı, shared-lab kapılı özellik". Laptop'ta ekstra UX sürtünmesi yok; lab sunucusunda flag açılınca Tenant MVP devreye girer.

**`SHARED_LAB` davranış şeması** (uygulanacak sözleşme):

```
SHARED_LAB=false  (default)
  → bugünkü davranış birebir korunur
  → X-Studio-Session-Id header'ı gelirse yok sayılır (no-op)

SHARED_LAB=true
  → X-Studio-Session-Id header'ı zorunlu (yoksa 400)
  → TaskRun.OwnerSessionId doldurulur; List/Get/Run/Cancel/Delete owner-filtreli
    (mismatch → 404, bilgi sızdırılmaz)
  → workspace zorla /workspace/sessions/{sessionId}/ altına map edilir
    (WorkspacePathGuard effective root = session kökü)
  → SignalR JoinGroup öncesi task ownership kontrolü
  → Settings/Credentials write endpoint'leri 403 (admin CONSOLE_API_KEY ile bypass)
  → FAIL-FAST: SHARED_LAB=true iken CONSOLE_API_KEY boşsa uygulama AÇILMAZ
    (flag'i unutup anonim shared deploy yapmayı imkânsızlaştırır)
```

Güvenlik varsayılanı ilkesi: sıkı kurallar (fail-fast) **yalnız shared-lab profilinde** — laptop'ta sürtünme yaratmaz; shared'da unutulan flag güvensiz kuruluma yol açamaz.

**MVP tasarımı (Session + Ownership, flag arkasında)**: tarayıcıda üretilen `SessionId` (localStorage) + `X-Studio-Session-Id` header'ı; `TaskRun.OwnerSessionId` kolonu (nullable = legacy, flag kapalıyken null); owner mismatch → 404; workspace path'leri zorla session prefix'e map (**effective root** yaklaşımı — logical prefix'ten daha güvenli); demo/settings kilidi yukarıdaki şemada.

**Test matrisi (iki path de test edilir)**:
- Flag OFF smoke: bugünkü davranış birebir (session header'sız task create/run/cancel)
- Flag ON: iki farklı session → birbirinin task'ı 404, workspace ayrı, credential write 403
- Flag ON + `CONSOLE_API_KEY` boş → startup fail-fast

**Karar matrisi (arşiv — karar verildi, referans için)**: A hiçbir şey (laptop) / B workspace prefix / C B+ownership / D full auth / E demo-mode flag. Seçilen: **C+E, `SHARED_LAB` flag'i arkasında; default A**. Full auth (D) yalnız kalıcı/prod kurulumda.

**Kapsam dışı (MVP)**: OAuth/LDAP, disk quota, session expiry, per-user API quota.

**UYGULANDI (2026-07-20)** — kabul kriterlerinin tamamı canlıda doğrulandı:
- ✅ Flag OFF: session header'sız task list/create birebir eski davranış (200/201)
- ✅ Flag ON: header'sız istek 400; öğrenci B, öğrenci A'nın task'ını göremez/iptal edemez (404); B'nin listesi boş; workspacePath `/workspace/sessions/{sid}/...`'e map edildi (DB'de doğrulandı); öğrenci credential write 403; skill suggest 200 (açık); admin key ile tüm task'lar görünür
- ✅ Fail-fast: `SHARED_LAB=true` + boş `CONSOLE_API_KEY` → startup exception, API servis vermiyor
- ✅ 113 unit test (SharedLabPolicy: session id charset, path mapping idempotency, yabancı session prefix'i reddi, admin-gate matrisi, fail-fast)

Uygulama parçaları: `SharedLabPolicy` + `SharedLabOptions` (Application), `ApiKeyMiddleware` profil-farkındalı (admin kimliği + session zorunluluğu + write kilidi), `TaskRun.OwnerSessionId` (migration `SharedLabTaskOwnership`), TasksController owner filtreleri, WorkspaceController effective root, ConsoleHub SubscribeTask ownership, frontend session kimliği (localStorage + interceptor + hub query).

---

## 2. Orchestrator refactor (god-class) — ✅ KAPANDI (2026-07-20, Sprint B)

**Durum (önce)**: `AgentOrchestratorService.cs` ~1450 satır; task lifecycle + pipeline + tool loop + model chain + prompt building + export + telemetri tek sınıfta.

**İlkeler**: davranış değişikliği YOK (feature eklemek yasak); public static test yüzeyi korunur (`ExportCodeBlocks`, `BuildModelChain`, `ShouldFallbackToNextModel`, `ShouldRequeueAfterCancellation`); küçük PR dilimleri.

**PR dilimleri** (sırayla):

| PR | Kapsam | Risk | Effort |
|----|--------|------|--------|
| R1 `CodeBlockExporter` | Export + regex + `IsValidFilename` | Düşük | 0.25–0.5 gün |
| R2 `ModelChainExecutor` | Chain walk, retry, sticky model, fallback event | Orta | 0.5–1 gün |
| R3 `CoderToolLoopRunner` | Loop, tool execute, 🔧 events, graceful finish | Orta-yüksek | 0.5–1 gün |
| R4 `AgentPromptBuilder` + telemetri (ops.) | Salt string/JSON | Düşük | 0.25–0.5 gün |

**Yapılmayacaklar**: refactor sırasında fix loop eklemek, message formatını değiştirmek, limitleri "iyileştirmek", repository pattern'e geçmek.

**Kabul**: 79+ test yeşil (aynı assert'ler); canlı tool-loop task Completed; cancel mid-call anında; fallback event formatı aynı; koordinatör sınıf < ~500 satır.

**UYGULANDI (2026-07-20)** — dilim başına ayrı commit, davranış değişikliği yok:
- R1 `CodeBlockExporter` (`a1fade7`): legacy export + regex + uzantı seti + limitler; testler yeni tipe aynı assert'lerle taşındı
- R2 `ModelChainExecutor` + `RunTelemetry.BuildErrorPayload` (`00d8a95`): chain walk, retry, sticky model desteği, fallback event'leri; `BuildModelChain`/`ShouldFallbackToNextModel` public static test yüzeyi yeni tipte
- R4 `AgentPromptBuilder` + `RunTelemetry` genişletme (`552ce4a`): mesaj kurulumu, rol talimatları, skills bloğu, context parse, request metadata; payload/cost/hash/trim yardımcıları
- R3 `CoderToolLoopRunner` (`9f7e7a8`): tool loop tamamı — ModelChainExecutor + AgentPromptBuilder + RunTelemetry + AgentWorkspaceTools + CodeBlockExporter kompozisyonu
- Sonuç: `AgentOrchestratorService` **1442 → 487 satır** (ince koordinatör); 113 test aynı assert'lerle yeşil; canlı tool-loop smoke + hedef mimari §2.4'teki çizimle birebir

---

## 3. Frontend testleri — ✅ KAPANDI (2026-07-20, Sprint C)

**Durum**: Angular 21 + `@angular/build:unit-test` (Vitest, jsdom). `npm test` → **27/27 yeşil**.

**Altyapı**:
- `angular.json` → `test` target (`runner: vitest`, `watch: false`)
- `package.json`: `test` / `test:watch`; devDeps: `vitest`, `jsdom`, `@angular/platform-browser-dynamic`
- `tsconfig.spec.json` + localStorage mock helper (`src/test-localstorage.ts`) for Node env

**Davranış çıkarımları (Studio flake'siz pure helpers)** — component hâlâ aynı UI; kurallar test edilebilir:

| Helper | Sorumluluk |
|--------|------------|
| `skill-selection.ts` | mergeSelectedSkillIds, applySkillToggle, isAutoSuggestedSkill |
| `debounced-action.ts` | DebouncedAction (600ms), shouldRequestSkillSuggestions (≥12 char) |
| `studio-run-state.ts` | pending/running transitions (create/run/cancel/rerun/poll) |

**MVP spec seti (hepsi yazıldı)**:
1. ✅ Debounce — 600ms öncesi yok; ardışık yazımda yalnız son değer; cancel
2. ✅ Skill merge — manual+auto unique; dismiss; toggle matrisi
3. ✅ Run/create error path state — stuck spinner kilidi (`onRunTaskError` / `onCreateTaskError`)
4. ✅ Cancel/rerun path — cancel error flags korur; rerun error flags temizler
5. ✅ apiKeyInterceptor — session header her zaman; X-Api-Key opsiyonel
6. ✅ ConsoleStreamService — setEvents / reset (SignalR connect bilinçli test dışı)
7. ✅ studio-session — path-safe id üretimi + reuse

**Test edilmeyecek (bilinçli)**: SCSS/layout, icon render, tam SignalR E2E / Playwright.

**CI komutu**:
```bash
dotnet test backend/tests/OmniAgentConsole.UnitTests/OmniAgentConsole.UnitTests.csproj
cd frontend && npm ci && npm test
```

---

## 4. Credential'ların Vault secret-ref modeline taşınması — ✅ KAPANDI (2026-07-20, Sprint D)

**Durum (önce)**: OmniAgent default key Vault'ta; `api_credentials.ApiKey` düz metin.

**Uygulanan model**:
- `ApiCredential`: `ApiKeySecretPath` + `ApiKeySecretKey` + `KeyLastFour`; `ApiKey` nullable (legacy / seed placeholder)
- `ISecretStore.IsWritable` + `DeleteAsync`; Vault writable, Environment read-only lab fallback
- `ApiCredentialSecretPolicy` (pure) + `ApiCredentialKeyResolver` (dual-read, Persist, MigratePlaintextKeys)
- `CredentialsController` create/update → PersistKeyAsync (Vault'ta path yazar, kolonu temizler)
- Request metadata: `apiCredentialId` / `agentDefinitionId` — **raw key yok**; `OmniAgentModelProvider` resolve eder
- Startup: `MigratePlaintextKeysAsync` gerçek key'leri Vault'a taşır; `YOUR_…_HERE` seed placeholder'lara dokunmaz
- Migration `CredentialSecretRefs` (idempotent SQL)

**Kabul (doğrulandı)**:
- ✅ Vault açık stack'te gerçek NVIDIA NIM key: `ApiKey` boş, `ApiKeySecretPath=providers/credentials/…`, `KeyLastFour` dolu
- ✅ Seed placeholder'lar path'siz + unconfigured
- ✅ 122 unit test (policy mask/placeholder/path)

**Bilinçli bırakılanlar**: `ApiKey` kolonu drop edilmedi (placeholder + non-Vault lab dual-read için); agent `CustomApiKey` legacy dual-read (metadata'sız, agentDefinitionId ile). Production'da real Vault (dev-mode değil) hâlâ checklist.

---

## 5. Opsiyonel ürün kalemleri

### 5.1 Reviewer → Coder fix loop — ✅ KAPANDI (2026-07-20, Sprint E)
Reviewer sonrası **en fazla bir** Coder fix pass:
- `ReviewerFixLoopPolicy.ShouldRunFixLoop` (pure heuristic: severity/bulgu marker'ları, bullet list; "no findings"/LGTM → skip)
- Console: `Fix loop started: … (single pass)` / `Fix loop skipped (no findings).`
- `CoderToolLoopRunner` `objectiveOverride` + display name `" (fix loop)"`; OpsMonitor zincirin en sonunda (fix çıktısı previousOutputs'ta)
- Unit testler: clean bill / severity / TR markers / bullet list / objective metni

### 5.2 bash / run_tests tool
Bilinçli YOK — sandbox'sız shell = RCE. İleride: ayrı sidecar container (network none, CPU/mem/timeout limitli). Ayrı proje.

### 5.3 OpenSearch
Compose profili var, log ship yok. Ya serilog sink bağla ya "future" olarak kalsın. Akademi önceliği düşük.

### 5.4 Reasoning "kaçının" listesi
Kod fallback'i eklendi; README listesi temkinli uyarı taşıyor. İş: 2 modelle canlı smoke → listeyi güncelle (1–2 saat).

---

## 6. Sprint planı

| Sprint | İçerik | Süre |
|--------|--------|------|
| A1 ✅ | Dual-deployment kararını belgele (bu belge + README) | tamam |
| A2 ✅ | `SHARED_LAB` flag'li Tenant MVP (session + ownership + prefix + fail-fast) | tamam (2026-07-20) — 113 test + iki-path canlı doğrulama |
| B ✅ | Orchestrator PR-R1→R4 | tamam (2026-07-20) — 1442→487 satır, 113 test, canlı smoke |
| C ✅ | Frontend test altyapısı + MVP specs | tamam (2026-07-20) — Vitest, 27 frontend test |
| D ✅ | Vault secret-ref credentials | tamam (2026-07-20) — dual-read + migrate + 122 test |
| E ✅ | Reviewer→Coder fix loop | tamam (2026-07-20) — tek pass, policy + orchestrator |

Açık çekirdek sprint yok.

---

## 7. Karar kayıtları (neden şimdi değil)

| Kalem | Gerekçe |
|-------|---------|
| Tenant | ~~Deployment modeline bağlı karar~~ → **Karar verildi (2026-07-20): dual profile** — default laptop-only, shared-lab `SHARED_LAB=true` ile opt-in; MVP implementasyonu Sprint A2 |
| Orchestrator split | ~~Regresyon riski ertelemesi~~ → **Sprint B tamam (2026-07-20)**: 1442→487 satır, 4 dilim commit |
| Frontend specs | ~~Altyapı + flake~~ → **Sprint C tamam (2026-07-20)**: pure helpers + Vitest 27 test |
| Vault credential path | ~~Loopback yeterli~~ → **Sprint D tamam (2026-07-20)**: secret-ref + migrate; prod'da real Vault hâlâ checklist |
| Full DLQ | Redelivered×2 lab için yeterli; `x-dead-letter-exchange` üstüne eklenebilir |
| Full OAuth | Akademi scope dışı |

---

## 8. Kapanmış bulgular arşivi (tekrar açılmasın)

1. **Cancel**: API → Redis publish (`task-cancellations`) → worker `RedisTaskCancelSubscriber` → local CTS iptali; DB'ye `Cancelled` token'dan önce; tool loop iterasyon başına DB check. Canlıda <1 sn doğrulandı.
2. **Recovery**: `RecoverInterruptedTaskRunsAsync` yalnız non-RabbitMQ (tek süreç) modunda.
3. **Poison**: `QueueMessage.Redelivered`; ikinci beklenmeyen fail → task Failed + ACK.
4. **Ports**: `INFRA_BIND_ADDRESS` default `127.0.0.1` (Postgres/Redis/RabbitMQ/Vault/OpenSearch).
5. **Sanitizer**: Anthropic/Google/GitHub/Slack/JWT/key=value pattern'leri; yalnız değer maskelenir; prose dokunulmaz (test edilmiş).
6. **Reasoning fallback**: content boşsa `reasoning_content`/`reasoning`.
7. **Git**: baseline `4fd390c`.
8. **Docs**: agent.md §21 + §14–15 historical notları; README + in-app Docs senkron.
9. **Agentic tool loop** (önceki oturum): Coder write_file/read_file/list_files araçlarıyla çok-turlu çalışır; sticky model; graceful finish; fence fallback; `output/` klasörü. Canlı E2E doğrulandı (6 dosya, deepseek→gpt-oss fallback dahil).

---

## 9. Agent Groups + moderated Panel (2026-08-12) — ✅ MVP

**Ürün kararı**: Studio pipeline’dan bağımsız persona grupları + otomatik floor’lu tek tur panel.

| Parça | Durum |
|--------|--------|
| Groups CRUD + Role/Stance/persona | ✅ |
| Panel session GUID, SignalR stream, queue kind | ✅ |
| Roster briefing + no invent guests | ✅ |
| Deep links `/groups/{id}`, `/panel/{id}` | ✅ |
| Key preflight + default credential / Settings dual-write | ✅ |
| Vault 512M cold-start | ✅ |

**Release notes**: [CHANGELOG.md](../CHANGELOG.md) § 2026-08-12 Panel.

---

## 10. Backlog — öncelikli task’lar (sırayla)

| ID | Task | Kabul | Durum |
|----|------|--------|--------|
| **T1** | Vault/key dayanıklılığı: startup’ta `OMNIAGENT_API_KEY` env → Vault seed; Panel key banner | Restart sonrası key dolu env ile panel/studio açılır | ✅ |
| **T2** | Birleşik History: task + panel session listesi, GUID link | `/history` her iki türü gösterir | ✅ |
| **T3** | Panel: 2. tur (N rounds) + kullanıcı mesajı (`continue`) | UI’dan rounds; bitince “user follow-up” ile ek tur | ✅ |
| **T4** | Panel transcript export (Markdown) | `GET …/transcript` + UI indir | ✅ |
| **T5** | Group clone + “Open in Panel” | Clone API/UI; Groups’tan Panel’e deep link | ✅ |
| T6 | LLM moderatör / manuel floor | Opsiyonel | 🔮 |
| T7 | Studio pipeline picker | Opsiyonel | 🔮 |
| T8 | Shared-lab öğrenci shell sadeleştirme | Opsiyonel | 🔮 |
| T9 | TTS / sesli panel | Future | 🔮 |

Güncelleme kuralı: her task bitince bu tablo + CHANGELOG + kısa README notu; **push yok** (local commits).

### UI polish (2026-08-12) — ✅ (scoped, not a redesign)

- Sidebar IA: **Build** (Studio, Workspace, Agents) · **Debate** (Groups, Panel) · **Ops** (History, Dashboard, Settings, Docs)
- Shared tokens + kit: `frontend/src/styles/_tokens.scss`, `_kit.scss` (`.oa-btn`, `.oa-card`, `.oa-alert`, `.oa-tag`, …)
- Dark NIM green identity kept; active nav left-accent
- Themes: dark / blue / white; Home dashboard; Panel conversation filter; durable file secret mirror
- Panel speaking bar + auto-scroll; Home key badge; `scripts/smoke-panel.sh`; Docs Panel how-to
- Floor progress bar; panel delete; `make smoke`; SCSS budgets; `docs/PR_BODY.md` (no push)
- Queue-busy hint; TTS Read; collapsed roster; bulk-delete finished panels
