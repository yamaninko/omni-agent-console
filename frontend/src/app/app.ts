import { Component, inject, signal } from '@angular/core';
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
  Palette
} from 'lucide-angular';
import { DialogHostComponent } from './core/ui/dialog-host.component';
import { AppTheme, ThemeService } from './core/ui/theme.service';

@Component({
  selector: 'app-root',
  imports: [LucideAngularModule, RouterLink, RouterLinkActive, RouterOutlet, DialogHostComponent],
  templateUrl: './app.html',
  styleUrl: './app.scss'
})
export class App {
  private readonly themes = inject(ThemeService);

  protected readonly title = signal('OmniAgent Console');
  protected readonly collapsed = signal(false);
  protected readonly theme = this.themes.theme;
  protected readonly themeOptions = this.themes.options;
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
    palette: Palette
  };

  protected setTheme(theme: AppTheme): void {
    this.themes.setTheme(theme);
  }
}
