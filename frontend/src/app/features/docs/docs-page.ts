import { Component, signal } from '@angular/core';
import { LucideAngularModule, BookOpen, Cpu, Workflow, Terminal, Server, Shield, RefreshCw, Sparkles, Layers } from 'lucide-angular';

@Component({
  selector: 'app-docs-page',
  imports: [LucideAngularModule],
  templateUrl: './docs-page.html',
  styleUrl: './docs-page.scss'
})
export class DocsPage {
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
}
