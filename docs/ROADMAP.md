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
| 5 | Tenant / sınıf izolasyonu | 🔲 Açık | §1 |
| 6 | InputSanitizer darlığı | ✅ Kapandı (kısmi, doğası gereği) | Genişletildi; pattern-dışı secret teorik risk |
| 7 | Orchestrator god-class | 🔲 Açık | §2 |
| 8 | Reasoning-only boş content | ✅ Kapandı | `reasoning_content`/`reasoning` fallback |
| 9 | Frontend test yok | 🔲 Açık | §3 |
| 10 | İlk commit | ✅ Kapandı | `4fd390c` |
| 11 | Docs drift (agent.md) | ✅ Kapandı | §14–15 historical, §21 eklendi |
| — | Reviewer→Coder fix loop | ⭐ Opsiyonel | §5.1 |
| — | OpenSearch entegrasyonu | 🔮 Future | Profil arkasında, log ship yok |

**Önerilen öncelik**: lab modeli kararı → (shared-server ise) Tenant MVP → Orchestrator refactor → Frontend specs → Vault secret-ref → (opsiyonel) Fix loop.

---

## 1. Tenant / sınıf izolasyonu

**Problem**: Sistem tek paylaşımlı tenant varsayar — tek `CONSOLE_API_KEY` (veya anonim), owner'sız task'lar, ortak workspace, global credentials. Her öğrenci kendi laptop'unda çalışıyorsa sorun yok; tek sunucu + sınıf Wi-Fi senaryosunda öğrenciler birbirinin task'ını iptal edebilir, workspace'ini silebilir.

**Karar matrisi**:

| Seçenek | Sağladığı | Karmaşıklık | Uygunluk |
|---------|-----------|-------------|----------|
| A. Hiçbir şey | Laptop-başına demo | 0 | En yaygın lab |
| B. Per-session workspace prefix | Dosya izolasyonu | ~0.5–1 gün | Hafif ortak sunucu |
| C. B + task ownership | Task list/cancel/delete izolasyonu | ~1–1.5 gün | **Ortak sunucu önerilen MVP** |
| D. Full auth (kullanıcı/rol) | Gerçek multi-tenant | 3–5+ gün | Production; akademi için aşırı |
| E. Demo mode flag | Credentials read-only, settings kilitli | Düşük | Hoca makinesi senaryosu |

**Karar yolu**: laptop-başına → A (belgele, kapat). Ortak sunucu → **C + E**. Full auth yalnız kalıcı kurulumda.

**MVP tasarımı (Session + Ownership)**: tarayıcıda üretilen `SessionId` (localStorage) + `X-Studio-Session-Id` header'ı; `TaskRun.OwnerSessionId` kolonu (nullable = legacy); List/Get/Cancel/Delete/Run'da owner eşleşmesi (mismatch → 404, bilgi sızdırmadan); workspace path'leri zorla `/workspace/sessions/{sessionId}/` altına map (**WorkspacePathGuard effective root = session kökü** — logical prefix'ten daha güvenli); SignalR JoinGroup öncesi ownership kontrolü; demo mode'da credential write 403.

**Kapsam dışı (MVP)**: OAuth/LDAP, disk quota, session expiry, per-user API quota.

**Effort**: ~1.5–2.5 gün. **Kabul**: iki session birbirinin task'ını görmez/iptal edemez; workspace yazma session dışına çıkamaz; demo mode'da credential write 403; README'de deployment modelleri.

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
| A | Lab modeli kararı + (shared ise) Tenant MVP + docs | 2–3 gün |
| B | Orchestrator PR-R1→R3 (+R4) | 1.5–3 gün |
| C | Frontend test altyapısı + spec 1–4 + CI | 1.5–2.5 gün |
| D | Vault dual-read → migrate → drop plaintext | 2–3.5 gün |
| E (ops.) | Reviewer→Coder fix loop | 1 gün |

Laptop-only ise A = "README'de belgele"; çekirdek B+C ≈ 3–5.5 gün.

---

## 7. Karar kayıtları (neden şimdi değil)

| Kalem | Gerekçe |
|-------|---------|
| Tenant | Deployment modeline bağlı tasarım kararı; yanlış model boşa efor |
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
