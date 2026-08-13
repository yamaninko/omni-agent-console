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
    'lang.label': 'Language'
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
    'lang.label': 'Dil'
  }
};

@Injectable({ providedIn: 'root' })
export class I18nService {
  private readonly localeSignal = signal<AppLocale>(this.readStored());

  readonly locale = this.localeSignal.asReadonly();
  readonly dict = computed(() => DICT[this.localeSignal()]);

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
