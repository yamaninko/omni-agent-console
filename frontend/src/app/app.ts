import { Component, signal } from '@angular/core';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { Bot, Gauge, History, LucideAngularModule, Settings, SquareTerminal, BookOpen, ChevronLeft, ChevronRight, FolderClosed } from 'lucide-angular';

@Component({
  selector: 'app-root',
  imports: [LucideAngularModule, RouterLink, RouterLinkActive, RouterOutlet],
  templateUrl: './app.html',
  styleUrl: './app.scss'
})
export class App {
  protected readonly title = signal('OmniAgent Console');
  protected readonly collapsed = signal(false);
  protected readonly icons = {
    bot: Bot,
    dashboard: Gauge,
    history: History,
    settings: Settings,
    studio: SquareTerminal,
    docs: BookOpen,
    chevronLeft: ChevronLeft,
    chevronRight: ChevronRight,
    workspace: FolderClosed
  };
}
