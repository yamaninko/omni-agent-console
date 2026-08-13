import { DatePipe } from '@angular/common';
import { Component, OnInit, inject, signal } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import {
  History,
  KeyRound,
  LucideAngularModule,
  MessagesSquare,
  SquareTerminal,
  Users,
  Sparkles
} from 'lucide-angular';
import { forkJoin, of } from 'rxjs';
import { catchError } from 'rxjs/operators';
import { TaskApiClient } from '../../core/api/task-api-client';
import { I18nService } from '../../core/i18n/i18n.service';
import { ApiCredential, PanelSessionSummary, StudentQuota, TaskSummary } from '../../core/models';

interface ActivityRow {
  kind: 'task' | 'panel';
  id: string;
  title: string;
  status: string;
  createdAt: string;
  link: string[];
}

@Component({
  selector: 'app-home-page',
  imports: [DatePipe, RouterLink, LucideAngularModule],
  templateUrl: './home-page.html',
  styleUrl: './home-page.scss'
})
export class HomePage implements OnInit {
  private readonly api = inject(TaskApiClient);
  private readonly router = inject(Router);
  private readonly i18n = inject(I18nService);

  protected readonly icons = {
    studio: SquareTerminal,
    panel: MessagesSquare,
    groups: Users,
    history: History,
    key: KeyRound,
    sparkles: Sparkles
  };

  protected readonly loading = signal(true);
  protected readonly apiKeyConfigured = signal<boolean | null>(null);
  protected readonly secretStore = signal<string | null>(null);
  protected readonly keyPreview = signal<string | null>(null);
  protected readonly activity = signal<ActivityRow[]>([]);
  protected readonly taskCount = signal(0);
  protected readonly panelCount = signal(0);
  protected readonly groupCount = signal(0);
  protected readonly sharedLab = signal(false);
  protected readonly isAdmin = signal(true);
  protected readonly quota = signal<StudentQuota | null>(null);
  protected readonly demoBusy = signal(false);
  protected readonly demoMessage = signal<string | null>(null);

  protected t(key: string): string {
    return this.i18n.t(key);
  }

  ngOnInit(): void {
    this.api.getSettings().subscribe({
      next: (s) => {
        this.apiKeyConfigured.set(!!s.apiKeyConfigured);
        this.secretStore.set(s.secretStore ?? null);
        this.sharedLab.set(!!s.sharedLabEnabled);
        this.isAdmin.set(s.isAdmin !== false);
        this.quota.set(s.quota ?? null);
      },
      error: () => this.apiKeyConfigured.set(null)
    });

    this.api.listCredentials().subscribe({
      next: (creds) => this.keyPreview.set(this.pickKeyPreview(creds)),
      error: () => this.keyPreview.set(null)
    });

    forkJoin({
      tasks: this.api.listTasks().pipe(catchError(() => of([] as TaskSummary[]))),
      panels: this.api.listPanels().pipe(catchError(() => of([] as PanelSessionSummary[]))),
      groups: this.api.listAgentGroups().pipe(catchError(() => of([])))
    }).subscribe({
      next: ({ tasks, panels, groups }) => {
        this.taskCount.set(tasks.length);
        this.panelCount.set(panels.length);
        this.groupCount.set(groups.length);
        const rows: ActivityRow[] = [
          ...tasks.slice(0, 8).map((t) => ({
            kind: 'task' as const,
            id: t.id,
            title: t.title,
            status: t.status,
            createdAt: t.createdAt,
            link: ['/tasks', t.id]
          })),
          ...panels.slice(0, 8).map((p) => ({
            kind: 'panel' as const,
            id: p.id,
            title: p.title || p.topic,
            status: p.status,
            createdAt: p.createdAt,
            link: ['/panel', p.id]
          }))
        ]
          .sort((a, b) => new Date(b.createdAt).getTime() - new Date(a.createdAt).getTime())
          .slice(0, 10);
        this.activity.set(rows);
        this.loading.set(false);
      },
      error: () => this.loading.set(false)
    });
  }

  private pickKeyPreview(creds: ApiCredential[]): string | null {
    const configured = creds.filter((c) => c.apiKeyConfigured);
    if (configured.length === 0) return null;
    const preferred =
      configured.find((c) => c.isDefault) ||
      configured.find((c) => /omni|nvidia/i.test(c.provider) || /nvidia/i.test(c.name)) ||
      configured[0];
    return preferred.maskedApiKey || (preferred.name ? `${preferred.name} · configured` : 'configured');
  }

  protected seedSampleDebate(): void {
    if (this.demoBusy()) return;
    this.demoBusy.set(true);
    this.demoMessage.set(null);
    this.api.seedDemoDebate().subscribe({
      next: (res) => {
        this.demoBusy.set(false);
        this.demoMessage.set(
          res.created
            ? `Created “${res.groupName}” — opening Panel…`
            : `Using existing “${res.groupName}” — opening Panel…`
        );
        void this.router.navigate(['/panel'], {
          queryParams: { groupId: res.groupId, topic: res.suggestedTopic }
        });
      },
      error: (err) => {
        this.demoBusy.set(false);
        this.demoMessage.set(err?.error || 'Demo seed failed');
      }
    });
  }

  protected openSampleStudio(presetId: string = 'fastapi-notes'): void {
    if (this.demoBusy()) return;
    this.demoBusy.set(true);
    this.demoMessage.set(null);
    this.api.getStudioDemoPreset(presetId).subscribe({
      next: (p) => {
        this.demoBusy.set(false);
        void this.router.navigate(['/studio'], {
          queryParams: {
            preset: p.id,
            pipeline: p.pipeline,
            workspace: p.workspacePath,
            prompt: p.prompt
          }
        });
      },
      error: () => {
        this.demoBusy.set(false);
        void this.router.navigate(['/studio'], {
          queryParams: { preset: 'fastapi-notes', pipeline: 'coder' }
        });
      }
    });
  }
}
