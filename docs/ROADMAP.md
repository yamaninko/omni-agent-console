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
| 4b | Credential at-rest (Vault secret-ref) | 🔲 Açık | §4 |
| 5 | Tenant / sınıf izolasyonu | ✅ Kapandı (2026-07-20) | §1 — dual deployment uygulandı: laptop default, shared-lab `SHARED_LAB=true` ile; iki-path canlı doğrulama yapıldı |
| 6 | InputSanitizer darlığı | ✅ Kapandı (kısmi, doğası gereği) | Genişletildi; pattern-dışı secret teorik risk |
| 7 | Orchestrator god-class | ✅ Kapandı (2026-07-20) | §2 — R1–R4 dilimleri uygulandı; 1442 → 487 satır, davranış değişmedi |
| 8 | Reasoning-only boş content | ✅ Kapandı | `reasoning_content`/`reasoning` fallback |
| 9 | Frontend test yok | 🔲 Açık | §3 |
| 10 | İlk commit | ✅ Kapandı | `4fd390c` |
| 11 | Docs drift (agent.md) | ✅ Kapandı | §14–15 historical, §21 eklendi |
| — | Reviewer→Coder fix loop | ⭐ Opsiyonel | §5.1 |
| — | OpenSearch entegrasyonu | 🔮 Future | Profil arkasında, log ship yok |

**Önerilen öncelik**: ~~lab modeli kararı~~ *(verildi: dual profile, bkz. §1)* → Tenant MVP (flag arkasında) → Orchestrator refactor → Frontend specs → Vault secret-ref → (opsiyonel) Fix loop.

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

## 2. Orchestrator refactor (god-class)

**Durum**: `AgentOrchestratorService.cs` ~1450 satır; task lifecycle + pipeline + tool loop + model chain + prompt building + export + telemetri tek sınıfta.

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

## 3. Frontend testleri

**Durum**: Angular 21, `ng test` script'i var, fiilen 0 spec. Studio ~660 satır (debounce, skill merge, run/cancel, poll). Geçmiş bug sınıfı: error path'te spinner kilitlenmesi.

**Altyapı** (bir kerelik, 0.5–1 gün): CLI default runner + TestBed + `HttpTestingController` + SignalR stub.

**MVP spec seti** (öncelik sırasıyla):
1. **Skill auto-suggest debounce** — 600ms öncesi çağrı yok; ardışık yazımda yalnız son değer; API error'da UI kilitlenmez.
2. **selectedSkillIds merge** — manual+auto unique; auto dismiss manual'ı korur. *(En yüksek ROI, en düşük flake.)*
3. **Run task error path** — create OK + run 500 → `pending=false, running=false` (stuck spinner kilidi).
4. **Rerun/cancel error path** — state tutarlı, throw yok.
5. *(ops.)* apiKeyInterceptor; 6. *(ops.)* ConsoleStreamService.

**Test edilmeyecek**: SCSS/layout, icon render, tam SignalR E2E (Playwright ayrı faz — akademi için lüks).

**CI**: `dotnet test` + `cd frontend && npm ci && npm test -- --watch=false` (headless). **Effort**: ~1.5–2.5 gün.

---

## 4. Credential'ların Vault secret-ref modeline taşınması

**Bugün**: OmniAgent/NVIDIA key'i Vault'ta (referans implementasyon: `ISecretStore` + `VaultSecretStore`); diğer provider key'leri `api_credentials.ApiKey` **düz metin**. Loopback bind ağ riskini kesti; disk/volume/backup erişimi hâlâ açık.

**Hedef model**: `ApiCredential`'a `ApiKeySecretPath` + `ApiKeySecretKey` + `KeyLastFour` (maskeleme için; list endpoint Vault'a gitmez); `ApiKey` kolonu deprecated → drop.

**Migrasyon** (sıfır downtime): ① schema ekle, `ApiKey` nullable ② dual-read (path dolu → Vault; boş → legacy + uyarı log) ③ one-shot job: plaintext'leri Vault'a taşı ④ dual-write kapat ⑤ ayrı migration'la kolonu düşür.

**Dokunulacak noktalar**: entity + migration, `CredentialsController` (Set/DeleteSecret), `ISecretStore.DeleteAsync`, `BuildRequestMetadata` (raw key metadata'da taşınmasın — provider'da resolve), seed placeholder'lar Vault'a yazılmaz.

**Agent CustomApiKey kararı**: **A** — credential FK zorunlu, free-text custom key kaldırılır (akademi için yeterli ve en temiz).

**Ek güvenlik**: production'da Vault dev-mode değil; token yalnız API+worker'a; credential update audit event'i.

**Effort**: ~2–3.5 gün. **Ne zaman zorunlu**: laptop lab → isteğe bağlı; ortak sunucu → önerilir; internete açık → zorunlu (+ TLS + real Vault).

**Kabul**: `api_credentials` tablosunda raw key yok (psql ile doğrula); custom provider key'li task çalışır; seed placeholder `configured=false`.

---

## 5. Opsiyonel ürün kalemleri

### 5.1 Reviewer → Coder fix loop (eğitim değeri yüksek)
Reviewer bulgu üretti ise **tek** ek Coder turu: messages'a findings + "yalnız bu bulguları düzelt, write_file ile" → OpsMonitor en sonda. Console event: `Fix loop started` / `Fix loop skipped (no findings)`. Limit tek tur (maliyet/loop riski). Effort ~1 gün; mimari hazır (tool loop + previousOutputs).

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
| B | Orchestrator PR-R1→R3 (+R4) | 1.5–3 gün |
| C | Frontend test altyapısı + spec 1–4 + CI | 1.5–2.5 gün |
| D | Vault dual-read → migrate → drop plaintext | 2–3.5 gün |
| E (ops.) | Reviewer→Coder fix loop | 1 gün |

A2 shared-lab kullanımından önce şart; yalnız laptop kullanılıyorsa ertelenebilir (flag zaten default kapalı olacak). Çekirdek B+C ≈ 3–5.5 gün.

---

## 7. Karar kayıtları (neden şimdi değil)

| Kalem | Gerekçe |
|-------|---------|
| Tenant | ~~Deployment modeline bağlı karar~~ → **Karar verildi (2026-07-20): dual profile** — default laptop-only, shared-lab `SHARED_LAB=true` ile opt-in; MVP implementasyonu Sprint A2 |
| Orchestrator split | Davranış değişikliği + büyük refactor aynı turda = regresyon riski; sınırlar netleşti, güvenle yapılabilir |
| Frontend specs | Altyapı + flake maliyeti; backend 79 test önce geldi |
| Vault credential path | Loopback bind ile lab riski düşürüldü; production checklist kalemi |
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
