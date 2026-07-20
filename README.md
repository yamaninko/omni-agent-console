# OmniAgent Console / Multi-Agent Studio

Web tabanlı, terminal hissiyatlı multi-agent studio. Backend .NET 10, frontend Angular 21; provider entegrasyonu OpenAI-compatible chat completion mantığıyla çalışır (NVIDIA NIM, OpenAI, Gemini OpenAI-compatible endpoint ve diğer uyumlu sağlayıcılar).

Bir prompt girersiniz; Planner → Research → Coder → Reviewer → (opsiyonel tek Coder **fix loop**) → Ops Monitor ajan zinciri çalışır, üretilen kod dosyaları `workspace/` klasörüne gerçek proje yapısıyla yazılır, tüm akış realtime console'da izlenir.

## Mevcut kapsam

### Mimari
- .NET 10 API + ayrı `OmniAgentConsole.Worker` süreci (task execution API process'inden bağımsız; API restart'ı çalışan task'ı öldürmez)
- PostgreSQL persistence ve EF Core migration
- RabbitMQ task queue: manual ack/nack ile **at-least-once teslimat** — host shutdown'da mesaj NACK'lenip requeue edilir, worker geri gelince task yeniden koşar; kullanıcı iptali ise ACK'lenir (canlı kesinti senaryosuyla doğrulandı)
- **Poison message koruması**: redelivered bir mesaj ikinci kez beklenmeyen hatayla düşerse task Failed + ACK yapılır — sonsuz requeue döngüsü olmaz (efektif max 2 teslimat)
- **Cross-process cancel**: iptal, Redis `task-cancellations` kanalıyla worker'a anında iletilir ve süren model HTTP çağrısı kesilir (canlıda <1 sn doğrulandı, 2026-07-20). DB'ye `Cancelled` token'dan önce yazılır ki worker iptali "user cancel" (ACK) olarak sınıflandırsın; Coder tool loop ek emniyet olarak her iterasyonda DB durumunu da kontrol eder
- API startup recovery yalnız **tek-süreç modunda** (RabbitMQ yokken) çalışır — ayrı worker varken API restart'ı canlı Running task'lara dokunmaz; ölü run'lar kuyruk redelivery ile kurtarılır
- SignalR realtime console stream; worker → API event akışı Redis pub/sub üzerinden
- Docker Compose ile frontend, API, agent-worker, PostgreSQL, Redis, RabbitMQ ve Vault; optional OpenSearch profili
- **Agentic tool loop (Coder)**: Coder ajanı Claude Code tarzı bir araç döngüsüyle çalışır — model `write_file` / `read_file` / `list_files` araçlarını çağırarak projeyi dosya dosya, kısa iterasyonlarla kurar (OpenAI-uyumlu function calling). Her tool call console'a canlı düşer, her iterasyon ayrı model çağrısı olarak usage'a işlenir; araç kullanmayan modeller için markdown fence export'u fallback olarak devrededir
- **Reviewer → Coder fix loop**: Reviewer eyleme dönük bulgu ürettiyse Coder **tek** ek turda yalnız o bulguları workspace üzerinde düzeltir (`Fix loop started` / `Fix loop skipped` console event'leri); Ops Monitor en sonda çalışır
- **Docker üretim kontratı**: Coder her backend projede `Dockerfile` + `docker-compose.yml` (service `app`, `${HOST_PORT:-18080}`, `/health`) üretir; Workspace runner bu kontratla ayağa kaldırır

### Skill Library (proje konvansiyon paketleri)
- 20 hazır skill: **Backend** (Node/Express/TS, Go, .NET, Java Spring Boot, Python FastAPI), **Frontend** (Angular, React, Flutter), **Data** (PostgreSQL + Migrations, ORM, MongoDB, Redis Caching, RabbitMQ Messaging), **Security** (JWT Authentication), **Quality** (Input Validation, Unit Tests, REST Conventions, Health Checks, README & Docs), **Packaging** (Dockerized Service)
- Studio'da seçilen skill'ler o task'ta **tüm ajanların system prompt'una zorunlu konvansiyon olarak enjekte edilir**; chip'e tıklayınca ne işe yaradığı açıklanır
- **Prompt'tan otomatik öneri**: yazarken (600ms debounce) prompt anahtar kelimelerle eşleştirilir, uyan skill'ler ✨ işaretiyle otomatik seçilir; istenmeyen chip tek tıkla düşürülür. Prompt belirsizse geri dönüş soruları gösterilir ("Hangi dil/framework?", "Hangi veritabanı?")
- Skill'ler Settings → Skill Library'den yönetilir; her skill'in `keywords` alanı otomatik önerinin neye tetikleneceğini belirler (kendi skill'leriniz de önerilebilir olur)

### Model yönetimi
- Model Registry: modeller UI'dan eklenip çıkarılabilir; **"Sync from NVIDIA"** butonu provider'ın OpenAI-uyumlu `GET /v1/models` kataloğunu (build.nvidia.com'daki tüm API modelleri, ~119 adet) tek tıkla içeri aktarır
- Agent başına max token limiti 200.000'e kadar ayarlanabilir (Agents ekranı)
- **Fallback model zinciri**: her ajan için 2 yedek model tanımlanabilir (Agents ekranı); primary model timeout/rate-limit/404 gibi hatalar verirse zincirdeki sonraki model otomatik denenir (yalnız 401 auth hatasında denenmez — aynı key tüm zinciri etkiler), console'a "falling back" event'i düşer

### Güvenlik
- Console API Key middleware: `CONSOLE_API_KEY` set edilirse tüm API + SignalR erişimi key ister (X-Api-Key, Bearer veya SignalR için `access_token` query); karşılaştırma timing-safe. Set edilmezse local dev için anonim erişim
- API Credentials Manager: sağlayıcı API key'leri UI'dan yönetilir; **API yanıtlarında raw key asla dönmez** (maskelenmiş önizleme + `apiKeyConfigured` bayrağı); update'te boş key mevcut key'i korur
- Workspace path guard: tüm dosya okuma/yazma/silme işlemleri `/workspace` köküne kilitlidir — `..` traversal, kök dışı mutlak path, backslash hilesi ve symlink kaçışları reddedilir; model çıktısından gelen dosya adları da aynı korumadan geçer
- Prompt/response redaction: `InputSanitizer` — NVIDIA/OpenAI/Anthropic (`sk-ant-`)/Google (`AIza`)/GitHub (`ghp_`, `github_pat_`)/Slack token'ları, JWT'ler, Bearer header'ları ve `PASSWORD=` / `JWT_SECRET=` / `"api_key": "..."` tarzı key=value atamaları maskelenir (yalnız değer; anahtar okunur kalır). Pattern-dışı serbest metin secret'ları teorik olarak kaçabilir — secret'ları prompt'a yazmayın
- **Infra portları varsayılan loopback**: Postgres/Redis/RabbitMQ/Vault/OpenSearch host portları `127.0.0.1`'e bind edilir (`INFRA_BIND_ADDRESS` ile değiştirilebilir, bkz. `.env.example`) — düz metin credential taşıyan servisler yerel ağa açılmaz

### Studio ve çıktılar
- Coder dosyaları **doğrudan workspace'e yazar** (tool loop, max 24 iterasyon / 50 dosya / dosya başına 1M karakter); tüm path'ler WorkspacePathGuard'dan geçer
- Tool desteklemeyen modellerde fallback: markdown fence'li bloklar ve `// filepath:` işaretli akış eski yöntemle export edilir; dosya adı tespit edilemeyen bloklar `output/` alt klasörüne düşer
- Workspace ekranı: üretilen dosyalar gezilir, okunur, silinir; klasör seçince **Project run** paneli — kopyalanabilir `docker compose up` komutu, **Start/Stop** (API → Docker socket), port ataması (`18000–18999`), `/health` linki
- Task history, task detail, dashboard; usage tracking (model, token, latency, hata)
- Agents ekranı: agent tanımları (model, system prompt, provider, credential bağlama, max token) UI'dan yönetilir

## Önerilen modeller (NVIDIA NIM free endpoint)

Katalogdaki adaylar gerçek çağrılarla test edildi (gecikme + çıktı formatı, 2026-07-17). Ajan başına öneri:

| Agent | Önerilen model | Neden |
|---|---|---|
| **Coder** | `deepseek-ai/deepseek-v4-flash` | Function calling'i güvenilir; agentic tool loop'ta çok-dosyalı projeyi write_file çağrılarıyla kurduğu canlı testte doğrulandı (2026-07-20). |
| **Planner** | `openai/gpt-oss-120b` | Planlama/akıl yürütmede güçlü, ~2.4s. Reasoning'i ayrı alanda döner, `content` temiz kalır. |
| **Reviewer** | `openai/gpt-oss-120b` | Coder'dan **farklı model ailesi olması bilinçli tercih** — aynı modelin kör noktalarını paylaşmaz, çapraz kontrol değeri katar. |
| **Research** | `nvidia/nemotron-3-super-120b-a12b` | NVIDIA'nın güncel agentic amiral modeli, ~2.6s; bağlam/kısıt çıkarma işine uygun. |
| **Ops Monitor** | `meta/llama-3.1-8b-instruct` | Kısa operasyonel özet için 8B yeterli ve hızlı; büyük model burada token/gecikme israfı. Alternatif: `stepfun-ai/step-3.7-flash` (~2.1s). |

**Kaçının** (test bulgusu):
- `qwen/qwen3.5-122b-a10b` — katalogdan kaldırıldı, endpoint **410 Gone** dönüyor (2026-07-20'de tespit edildi; önceki sürümlerde Coder primary'siydi)
- `moonshotai/kimi-k2.6`, `nvidia/nemotron-nano-3-30b-a3b` — katalogda listeleniyor ama endpoint **404** dönüyor (deploy edilmemiş)
- `nvidia/llama-3.3-nemotron-super-49b-v1.5`, `nvidia/nvidia-nemotron-nano-9b-v2` — cevabı `reasoning` alanına yazıp `content`'i **boş bırakıyor**. Provider artık content boşsa `reasoning_content`/`reasoning` alanına düşüyor (2026-07-20), yani bu modeller kullanılabilir hale geldi — yine de canlı doğrulama yapılana kadar primary olarak önermiyoruz
- `deepseek-ai/deepseek-v4-pro`, `z-ai/glm-5.2`, `mistralai/mistral-medium-3.5-128b` — free tier kuyruğunda 60–90s+ bekletiyor; 120s agent timeout'una sürekli dayanır

Uygulanan zincirler (primary → fallback 1 → fallback 2):

- Planner: `gpt-oss-120b` → `nemotron-3-super-120b-a12b` → `llama-3.1-8b-instruct`
- Research: `nemotron-3-super-120b-a12b` → `step-3.7-flash` → `llama-3.1-8b-instruct`
- Coder: `deepseek-v4-flash` → `gpt-oss-120b` → `nemotron-3-super-120b-a12b` (timeout 300s)
- Reviewer: `gpt-oss-120b` → `nemotron-3-super-120b-a12b` → `deepseek-v4-flash` (timeout 180s)
- Ops Monitor: `llama-3.1-8b-instruct` → `step-3.7-flash` → `minimax-m3`

Not: Gecikmeler free tier yoğunluğuna göre değişir; katalog güncellenir. "Sync from NVIDIA" sonrası yeni modelleri Agents ekranından deneyebilirsiniz.

## Proje yapısı

```text
backend/src/OmniAgentConsole.Api             # REST API, SignalR hub, middleware, startup seed/sync
backend/src/OmniAgentConsole.Application     # DTO'lar, guard'lar (WorkspacePathGuard), SkillSuggestionEngine, InputSanitizer
backend/src/OmniAgentConsole.Domain          # Entity'ler ve enum'lar
backend/src/OmniAgentConsole.Infrastructure  # EF Core, RabbitMQ, Redis, Vault, provider, orchestrator
backend/src/OmniAgentConsole.Worker          # Task execution süreci (queue consumer + orchestrator)
backend/tests/OmniAgentConsole.UnitTests     # xUnit (guard, export, requeue, suggestion, sanitizer testleri)
frontend                                     # Angular 21 studio
workspace                                    # agent çıktı dosyaları (git'e girmez)
```

## Gereksinimler

- Docker Desktop
- .NET 10 SDK
- Node.js 24 önerilir

Sadece Docker ile çalıştıracaksanız local .NET ve Node kurulumu zorunlu değildir.

## Konfigürasyon

```bash
cp .env.example .env
```

### OmniAgent (varsayılan provider) API key

1. Uygulama açıldıktan sonra `Settings` ekranından key girin. Key HashiCorp Vault içine yazılır.
2. Alternatif olarak `.env` dosyasına `OMNIAGENT_API_KEY=...` ekleyin. Vault dev mode resetlenirse backend bu env değerini fallback olarak kullanır.

### Diğer sağlayıcı key'leri

`Settings → API Credentials Manager` üzerinden eklenir (OpenAI, Anthropic, Gemini, Ollama, Custom/OpenAI-compatible). Key'ler PostgreSQL'de saklanır; API yanıtlarında yalnızca maskelenmiş önizleme döner. Bir credential "Default" işaretlenirse credential bağlanmamış agent'lar bu key'e düşer. Çağrılar OpenAI-compatible `/chat/completions` formatıyla yapılır; Anthropic native API şu an desteklenmez (Notlar'a bakın).

### Console API Key

`.env` içinde `CONSOLE_API_KEY=...` set edilirse backend tüm REST ve SignalR isteklerinde bu key'i ister; frontend `Settings` ekranındaki "Console API Key" alanına girilen değeri localStorage'da tutar ve isteklere ekler. Boş bırakılırsa (local dev varsayılanı) erişim anonimdir. Paylaşılan/production benzeri her ortamda set edilmelidir.

Local Vault bilgileri:

- Address: `http://localhost:8201`
- Token: `dev-root-token`
- Secret path: `secret/data/providers/omniagent`
- Secret reference: `secret/providers/omniagent#apiKey`

Dev mode Vault production için uygun değildir.

## Deployment modelleri

Aynı kod tabanı iki profili destekler; davranış ortam değişkeniyle seçilir:

| Profil | Ne zaman | İzolasyon | Gereken |
|--------|----------|-----------|---------|
| **Laptop-only** (varsayılan) | Her öğrenci/kullanıcı kendi makinesinde `docker compose up` | Gerekmez — tek kullanıcı | Hiçbir şey; bugünkü davranış |
| **Shared-lab** (opt-in) | Tek sunucu, sınıf aynı URL'e bağlanır | Session + task ownership + `/workspace/sessions/{id}/` prefix | `SHARED_LAB=true` + `CONSOLE_API_KEY` |

- **Laptop-only**: flag kapalı, ekstra kimlik/oturum sürtünmesi yok. Infra portları zaten `127.0.0.1`'e bind'lidir.
- **Shared-lab**: `SHARED_LAB=true` ile session header'ı zorunlu olur, task'lar oturum sahibine filtrelenir, workspace oturum köküne kilitlenir ve Settings/Credentials yazma uçları kilitlenir (eğitmen `CONSOLE_API_KEY` ile yönetir). Flag açıkken `CONSOLE_API_KEY` boşsa uygulama **fail-fast** ile açılmaz — anonim paylaşımlı kurulum kazara mümkün değildir.

> ✅ **Durum**: Shared-lab profili uygulandı ve canlı doğrulandı (2026-07-20): iki farklı oturum birbirinin task'ını göremez/iptal edemez (404), workspace `/workspace/sessions/{id}/` altına kilitlenir, öğrenci credential/agent/skill/settings yazamaz (403, skill auto-suggest açık), `SHARED_LAB=true` + boş `CONSOLE_API_KEY` kombinasyonunda uygulama açılmaz. Kullanım: `.env`'de `SHARED_LAB=true` ve `CONSOLE_API_KEY=<eğitmen-anahtarı>` set edip stack'i yeniden başlatın; eğitmen UI'da Settings → Console API Key alanına anahtarı girerek admin yetkisi kazanır.

## Docker ile çalıştırma

```bash
docker compose up -d --build
```

Compose proje adı `docker-compose.yml` içinde sabittir (`name: omni-agent-console`); container'lar `omni-agent-console-*` olarak adlandırılır. `agent-worker` servisi otomatik başlar; task'lar RabbitMQ üzerinden worker'a dağıtılır, console event'leri Redis pub/sub ile API'ye ve oradan SignalR ile arayüze akar.

Servisler:

- Frontend: `http://localhost:4210`
- Backend health: `http://localhost:5080/health`
- RabbitMQ UI: `http://localhost:15673`
- Vault API/UI: `http://localhost:8201`

Log izleme / durum:

```bash
docker compose logs -f backend-api agent-worker frontend
docker compose ps
curl http://localhost:5080/health
```

Opsiyonel OpenSearch:

```bash
docker compose --profile observability up -d --build
```

Docker profilinde task dispatch varsayılan olarak RabbitMQ kullanır; local backend'i RabbitMQ olmadan çalıştırmak için appsettings varsayılanı `InMemory` kalır.

## Kullanım akışı

1. `http://localhost:4210` adresini açın.
2. `Settings` ekranında OmniAgent API key girin (ve/veya API Credentials Manager'dan sağlayıcı key'leri ekleyin); `Check Health` ile doğrulayın.
3. İsteğe bağlı: `Settings → Model Registry → Sync from NVIDIA` ile tüm katalog modellerini içeri alın ve `Agents` ekranından ajan-model atamalarını yapın (yukarıdaki öneri tablosuna bakın).
4. `Studio` ekranında çalışma dizinini seçin ve prompt'u yazın — uygun skill'ler otomatik önerilir; gerekirse chip'lerden elle ekleyip çıkarın.

Örnek prompt:

```text
PostgreSQL veritabanından kullanıcı bilgilerini çeken ve sık sorgulanan verileri
Redis üzerinde cache'leyen, yüksek performanslı bir Go REST API yaz. Redis
bağlantısı için retry mekanizması ekle. Tüm yapıyı ayağa kaldıracak
docker-compose dosyasını ve health check endpoint'ini de hazırla.
```

(Bu prompt Go REST API, Redis Caching, PostgreSQL + Migrations, Dockerized Service ve Health Checks skill'lerini otomatik seçtirir.)

5. `Run Task` ile başlatın; realtime console'da ajan adımlarını izleyin.
6. Üretilen dosyaları `Workspace` ekranından, metrikleri `History` / `Task Detail` / `Dashboard` ekranlarından inceleyin.

## Local geliştirme

Backend API:

```bash
dotnet run --project backend/src/OmniAgentConsole.Api/OmniAgentConsole.Api.csproj
```

Worker (task execution için gereklidir; API tek başına task çalıştırmaz):

```bash
dotnet run --project backend/src/OmniAgentConsole.Worker/OmniAgentConsole.Worker.csproj
```

Frontend:

```bash
cd frontend
npm install
npm start
```

Test/build:

```bash
dotnet build OmniAgentConsole.slnx
dotnet test backend/tests/OmniAgentConsole.UnitTests/OmniAgentConsole.UnitTests.csproj
cd frontend && npm test          # Vitest unit tests (27)
cd frontend && npm run build
```

## Notlar

- Task execution ayrı worker process'indedir; worker restart'ında yarım kalan task'lar NACK/requeue ile otomatik yeniden koşar. API startup recovery'si yalnız tek-süreç modunda (RabbitMQ yokken) devrededir — ayrı worker topolojisinde API restart'ı canlı task'lara dokunmaz.
- Prompt/response kayıtlarında `InputSanitizer` temel PII/secret maskeleme uygular; provider raw metadata henüz redact edilmeden saklanır.
- Credentials API'si raw key döndürmez. Vault açıkken (Docker varsayılanı) provider key'leri `providers/credentials/{id}` secret path'inde saklanır; DB'de yalnız `ApiKeySecretPath` + `KeyLastFour` kalır (startup migrate mevcut düz metin key'leri taşır). Vault kapalı lab modunda legacy `ApiKey` kolonu kullanılır.
- Multi-provider desteği OpenAI-compatible endpoint'lerle sınırlıdır (NVIDIA NIM, OpenAI, Gemini'nin OpenAI-compatible endpoint'i, Ollama, Custom). Anthropic native API şeması desteklenmez.
- Provider, `content` boş geldiğinde `reasoning_content`/`reasoning` alanına düşer; reasoning-only modeller artık boş çıktı yerine kullanılabilir sonuç üretir.
- Schema tamamen EF migration'lardadır (`InitialCreate` + `CredentialsSkillsAndFallbacks` + `SharedLabTaskOwnership` + `CredentialSecretRefs`); idempotent SQL yazılanlar hem sıfır hem yamalı DB'lere temiz uygulanır. Startup kodu veri seed + (Vault açıksa) credential plaintext→secret migrate yapar. Migration üretmek için: `dotnet tool restore && dotnet ef migrations add <Ad> --project backend/src/OmniAgentConsole.Infrastructure --startup-project backend/src/OmniAgentConsole.Api --output-dir Persistence/Migrations`
- NVIDIA katalog senkronizasyonu context window bilgisi getirmez (`/v1/models` bu alanı sunmaz); kritik modeller için Settings'ten elle girilebilir.

## Yol haritası

Yol haritası ve kapanmış bulgu arşivi [docs/ROADMAP.md](docs/ROADMAP.md) dosyasındadır. Tamamlananlar: dual deployment / shared-lab, orchestrator refactor, frontend Vitest specs, credential Vault secret-ref, Reviewer→Coder fix loop.
