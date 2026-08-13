import { Component, OnInit, inject, signal } from '@angular/core';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import {
  Bot,
  Gauge,
  History,
  LucideAngularModule,
  Settings,
  SquareTerminal,
  BookOpen,
  ChevronLeft,
  ChevronRight,
  FolderClosed,
  Users,
  MessagesSquare,
  Palette,
  House
} from 'lucide-angular';
import { TaskApiClient } from './core/api/task-api-client';
import { AppLocale, I18nService } from './core/i18n/i18n.service';
import { DialogHostComponent } from './core/ui/dialog-host.component';
import { AppTheme, ThemeService } from './core/ui/theme.service';

@Component({
  selector: 'app-root',
  imports: [LucideAngularModule, RouterLink, RouterLinkActive, RouterOutlet, DialogHostComponent],
  templateUrl: './app.html',
  styleUrl: './app.scss'
})
export class App implements OnInit {
  private readonly themes = inject(ThemeService);
  private readonly api = inject(TaskApiClient);
  private readonly i18n = inject(I18nService);

  protected readonly title = signal('OmniAgent Console');
  protected readonly collapsed = signal(false);
  protected readonly theme = this.themes.theme;
  protected readonly themeOptions = this.themes.options;
  protected readonly locale = this.i18n.locale;
  /** When shared-lab is on and caller is not admin, hide config nav links. */
  protected readonly showAdminNav = signal(true);
  protected readonly sharedLabEnabled = signal(false);
  protected readonly icons = {
    bot: Bot,
    dashboard: Gauge,
    history: History,
    settings: Settings,
    studio: SquareTerminal,
    docs: BookOpen,
    chevronLeft: ChevronLeft,
    chevronRight: ChevronRight,
    workspace: FolderClosed,
    groups: Users,
    panel: MessagesSquare,
    palette: Palette,
    home: House
  };

  ngOnInit(): void {
    this.api.getSettings().subscribe({
      next: (s) => {
        const lab = !!s.sharedLabEnabled;
        this.sharedLabEnabled.set(lab);
        // Default isAdmin true when field missing (older API / single-user).
        this.showAdminNav.set(!lab || s.isAdmin !== false);
      },
      error: () => {
        this.showAdminNav.set(true);
        this.sharedLabEnabled.set(false);
      }
    });
  }

  protected t(key: string): string {
    return this.i18n.t(key);
  }

  protected setLocale(locale: AppLocale): void {
    this.i18n.setLocale(locale);
  }

  protected setTheme(theme: AppTheme): void {
    this.themes.setTheme(theme);
  }
}
