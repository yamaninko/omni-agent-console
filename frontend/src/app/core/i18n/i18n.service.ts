import { Injectable, computed, signal } from '@angular/core';

export type AppLocale = 'en' | 'tr';

const DICT: Record<AppLocale, Record<string, string>> = {
  en: {
    'nav.home': 'Home',
    'nav.studio': 'Studio',
    'nav.workspace': 'Workspace',
    'nav.agents': 'Agents',
    'nav.groups': 'Groups',
    'nav.panel': 'Panel',
    'nav.history': 'History',
    'nav.dashboard': 'Dashboard',
    'nav.settings': 'Settings',
    'nav.docs': 'Docs',
    'nav.build': 'Build',
    'nav.debate': 'Debate',
    'nav.ops': 'Ops',
    'nav.theme': 'Theme',
    'brand.subtitle': 'Multi-Agent Studio',
    'lang.label': 'Language',

    'home.eyebrow': 'Home',
    'home.title': 'OmniAgent Console',
    'home.sub':
      'Build multi-agent coding tasks in Studio, or run moderated debates in Panel — same stack, two surfaces.',
    'home.studentBanner':
      'Shared lab · student mode. API keys and agent config are managed by the instructor. Use Studio, Panel, Groups, Workspace, and History.',
    'home.keyOk': 'API key configured',
    'home.keyMissing': 'API key not configured — Panel/Studio will fail until you save a key.',
    'home.keyReadyServer': 'Provider key is ready on the server — you can run tasks.',
    'home.keyNotReadyServer': 'Provider key is not ready — ask the instructor to configure Settings.',
    'home.demoDebate': 'Run sample debate cast',
    'home.demoFastapi': 'Sample Studio · FastAPI',
    'home.demoDotnet': 'Sample Studio · .NET API',
    'home.quickStudio': 'Studio',
    'home.quickStudioSub': 'Code agents · skills · workspace',
    'home.quickPanel': 'Panel',
    'home.quickPanelSub': 'Moderated multi-persona debate',
    'home.quickGroups': 'Groups',
    'home.quickHistory': 'History',
    'home.activity': 'Recent activity',
    'home.viewAll': 'View all',
    'home.checklist': 'First-run checklist',
    'home.quota': 'Your lab quota',
    'home.quotaConcurrent': 'Concurrent tasks',
    'home.quotaDaily': 'Tasks today',
    'home.quotaTokens': 'Tokens today',

    'panel.eyebrow': 'Debate',
    'panel.title': 'Panel',
    'panel.sub':
      'Moderated roster · 1–3 rounds · ~60s generation budget per speaker (free-tier queue wait is separate).',
    'panel.inject': 'Audience inject (live)',
    'panel.injectBtn': 'Inject question',
    'panel.followUp': 'Your follow-up (extra round)',
    'panel.continue': 'Continue panel',
    'panel.vote': 'Who convinced you?',
    'panel.mic': 'Mic',
    'panel.micStop': 'Stop mic',

    'groups.eyebrow': 'Debate',
    'groups.title': 'Agent Groups',
    'groups.sub': 'Define who sits on the panel: role, stance, persona mission. Order = speaking order.',
    'groups.templates': 'Templates',
    'groups.markTemplate': 'Mark as template',
    'groups.unmarkTemplate': 'Unmark template',
    'groups.clone': 'Clone group'
  },
  tr: {
    'nav.home': 'Ana sayfa',
    'nav.studio': 'Stüdyo',
    'nav.workspace': 'Çalışma alanı',
    'nav.agents': 'Ajanlar',
    'nav.groups': 'Gruplar',
    'nav.panel': 'Panel',
    'nav.history': 'Geçmiş',
    'nav.dashboard': 'Pano',
    'nav.settings': 'Ayarlar',
    'nav.docs': 'Doküman',
    'nav.build': 'Üretim',
    'nav.debate': 'Tartışma',
    'nav.ops': 'Operasyon',
    'nav.theme': 'Tema',
    'brand.subtitle': 'Çoklu Ajan Stüdyosu',
    'lang.label': 'Dil',

    'home.eyebrow': 'Ana sayfa',
    'home.title': 'OmniAgent Console',
    'home.sub':
      'Stüdyo’da çoklu ajan kod görevleri veya Panel’de moderasyonlu tartışmalar — aynı yığın, iki yüzey.',
    'home.studentBanner':
      'Ortak lab · öğrenci modu. API anahtarları ve ajan ayarları eğitmendedir. Stüdyo, Panel, Gruplar, Çalışma alanı ve Geçmiş kullanın.',
    'home.keyOk': 'API anahtarı yapılandırıldı',
    'home.keyMissing': 'API anahtarı yok — kaydedene kadar Panel/Stüdyo başarısız olur.',
    'home.keyReadyServer': 'Sağlayıcı anahtarı sunucuda hazır — görev çalıştırabilirsiniz.',
    'home.keyNotReadyServer': 'Sağlayıcı anahtarı hazır değil — eğitmenden Ayarlar’ı yapılandırmasını isteyin.',
    'home.demoDebate': 'Örnek tartışma kadrosu',
    'home.demoFastapi': 'Örnek Stüdyo · FastAPI',
    'home.demoDotnet': 'Örnek Stüdyo · .NET API',
    'home.quickStudio': 'Stüdyo',
    'home.quickStudioSub': 'Kod ajanları · skill · workspace',
    'home.quickPanel': 'Panel',
    'home.quickPanelSub': 'Moderasyonlu çok kişilik tartışma',
    'home.quickGroups': 'Gruplar',
    'home.quickHistory': 'Geçmiş',
    'home.activity': 'Son etkinlik',
    'home.viewAll': 'Tümü',
    'home.checklist': 'İlk çalıştırma listesi',
    'home.quota': 'Lab kotanız',
    'home.quotaConcurrent': 'Eşzamanlı görev',
    'home.quotaDaily': 'Bugünkü görev',
    'home.quotaTokens': 'Bugünkü token',

    'panel.eyebrow': 'Tartışma',
    'panel.title': 'Panel',
    'panel.sub':
      'Moderasyonlu kadro · 1–3 tur · konuşmacı başına ~60 sn üretim bütçesi (ücretsiz kuyruk beklemesi ayrı).',
    'panel.inject': 'Seyirci sorusu (canlı)',
    'panel.injectBtn': 'Soru ekle',
    'panel.followUp': 'Takip mesajınız (ek tur)',
    'panel.continue': 'Panele devam',
    'panel.vote': 'Kim ikna etti?',
    'panel.mic': 'Mikrofon',
    'panel.micStop': 'Mikrofonu durdur',

    'groups.eyebrow': 'Tartışma',
    'groups.title': 'Ajan Grupları',
    'groups.sub': 'Panelde kim oturur: rol, tutum, persona görevi. Sıra = konuşma sırası.',
    'groups.templates': 'Şablonlar',
    'groups.markTemplate': 'Şablon olarak işaretle',
    'groups.unmarkTemplate': 'Şablonu kaldır',
    'groups.clone': 'Grubu kopyala'
  }
};

@Injectable({ providedIn: 'root' })
export class I18nService {
  private readonly localeSignal = signal<AppLocale>(this.readStored());

  readonly locale = this.localeSignal.asReadonly();
  readonly dict = computed(() => DICT[this.localeSignal()]);

  /** BCP-47 tag for Web Speech / STT. */
  speechLang(): string {
    return this.localeSignal() === 'tr' ? 'tr-TR' : 'en-US';
  }

  t(key: string): string {
    return this.dict()[key] ?? DICT.en[key] ?? key;
  }

  setLocale(locale: AppLocale): void {
    this.localeSignal.set(locale);
    try {
      localStorage.setItem('oa_locale', locale);
    } catch {
      /* ignore */
    }
  }

  private readStored(): AppLocale {
    try {
      const v = localStorage.getItem('oa_locale');
      if (v === 'tr' || v === 'en') return v;
    } catch {
      /* ignore */
    }
    return 'en';
  }
}
