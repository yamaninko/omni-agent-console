import { Component, inject, signal } from '@angular/core';
import { LucideAngularModule, BookOpen, Cpu, Workflow, Terminal, Server, Shield, RefreshCw, Sparkles, Layers } from 'lucide-angular';
import { I18nService } from '../../core/i18n/i18n.service';

@Component({
  selector: 'app-docs-page',
  imports: [LucideAngularModule],
  templateUrl: './docs-page.html',
  styleUrl: './docs-page.scss'
})
export class DocsPage {
  private readonly i18n = inject(I18nService);
  protected readonly activeTab = signal<'user' | 'tech'>('user');
  protected readonly icons = {
    book: BookOpen,
    cpu: Cpu,
    workflow: Workflow,
    terminal: Terminal,
    server: Server,
    shield: Shield,
    refresh: RefreshCw,
    sparkles: Sparkles,
    layers: Layers
  };

  protected t(key: string): string {
    return this.i18n.t(key);
  }
}
