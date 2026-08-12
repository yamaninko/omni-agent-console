import { DatePipe } from '@angular/common';
import { Component, OnDestroy, OnInit, inject, signal } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import {
  LucideAngularModule,
  MessageSquare,
  Play,
  Square,
  Users
} from 'lucide-angular';
import { Subscription, interval, switchMap, takeWhile } from 'rxjs';
import { TaskApiClient } from '../../core/api/task-api-client';
import { ConsoleStreamService } from '../../core/realtime/console-stream.service';
import {
  AgentGroupSummary,
  ConsoleEvent,
  PanelSessionDetail,
  PanelSessionSummary
} from '../../core/models';

@Component({
  selector: 'app-panel-page',
  imports: [LucideAngularModule, DatePipe],
  templateUrl: './panel-page.html',
  styleUrl: './panel-page.scss'
})
export class PanelPage implements OnInit, OnDestroy {
  private readonly api = inject(TaskApiClient);
  private readonly stream = inject(ConsoleStreamService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private pollSub?: Subscription;
  private routeSub?: Subscription;

  protected readonly icons = {
    chat: MessageSquare,
    play: Play,
    stop: Square,
    users: Users
  };

  protected readonly groups = signal<AgentGroupSummary[]>([]);
  protected readonly recent = signal<PanelSessionSummary[]>([]);
  protected readonly selectedGroupId = signal('');
  protected readonly topic = signal('');
  protected readonly title = signal('');
  protected readonly session = signal<PanelSessionDetail | null>(null);
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);

  protected readonly events = this.stream.events;

  ngOnInit(): void {
    this.api.listAgentGroups().subscribe({
      next: (g) => {
        this.groups.set(g);
        if (g.length && !this.selectedGroupId()) {
          this.selectedGroupId.set(g[0].id);
        }
      }
    });
    this.reloadRecent();

    // Deep-link: /panel/{guid} loads and keeps that session in the address bar.
    this.routeSub = this.route.paramMap.subscribe((params) => {
      const sessionId = params.get('sessionId');
      if (sessionId && sessionId !== this.session()?.id) {
        this.loadSessionById(sessionId);
      }
    });
  }

  ngOnDestroy(): void {
    this.pollSub?.unsubscribe();
    this.routeSub?.unsubscribe();
  }

  protected reloadRecent(): void {
    this.api.listPanels().subscribe({
      next: (p) => this.recent.set(p),
      error: () => undefined
    });
  }

  protected async startPanel(): Promise<void> {
    const groupId = this.selectedGroupId();
    const topic = this.topic().trim();
    if (!groupId || !topic) {
      this.error.set('Pick a group and enter a topic.');
      return;
    }

    this.busy.set(true);
    this.error.set(null);
    this.stream.reset();

    this.api.createPanel(groupId, topic, this.title().trim() || null).subscribe({
      next: (created) => {
        this.session.set(created);
        // Put the permanent session GUID in the URL immediately.
        void this.router.navigate(['/panel', created.id], { replaceUrl: true });
        this.api.startPanel(created.id).subscribe({
          next: async () => {
            this.busy.set(false);
            await this.stream.connect(created.id);
            this.api.getPanelEvents(created.id).subscribe({
              next: (ev) => this.stream.setEvents(ev)
            });
            this.startPolling(created.id);
            this.reloadRecent();
          },
          error: (err) => {
            this.busy.set(false);
            this.error.set(this.readError(err, 'Failed to start panel'));
            this.reloadRecent();
          }
        });
      },
      error: (err) => {
        this.busy.set(false);
        this.error.set(this.readError(err, 'Failed to create panel'));
      }
    });
  }

  private readError(err: unknown, fallback: string): string {
    const e = err as { error?: string | { title?: string; detail?: string }; message?: string };
    if (typeof e?.error === 'string' && e.error.trim()) {
      return e.error;
    }
    if (e?.error && typeof e.error === 'object') {
      return e.error.detail || e.error.title || fallback;
    }
    return e?.message || fallback;
  }

  protected cancelPanel(): void {
    const id = this.session()?.id;
    if (!id) return;
    this.api.cancelPanel(id).subscribe({
      next: () => {
        this.reloadSession(id);
        this.reloadRecent();
      },
      error: (err) => this.error.set(this.readError(err, 'Cancel failed'))
    });
  }

  protected openRecent(item: PanelSessionSummary): void {
    void this.router.navigate(['/panel', item.id]);
  }

  protected newSessionForm(): void {
    this.pollSub?.unsubscribe();
    this.stream.reset();
    this.session.set(null);
    this.error.set(null);
    void this.router.navigate(['/panel']);
  }

  protected isUserMessage(ev: ConsoleEvent): boolean {
    return ev.eventType === 'UserMessage';
  }

  protected isSpeech(ev: ConsoleEvent): boolean {
    return ev.eventType === 'PanelTurnCompleted';
  }

  protected isFloor(ev: ConsoleEvent): boolean {
    return ev.eventType === 'PanelFloorGranted';
  }

  /** Pre-speech roster / mission card written as AgentStep. */
  protected isRosterBriefing(ev: ConsoleEvent): boolean {
    return (
      ev.eventType === 'AgentStep' &&
      (ev.message?.includes('Panel briefing') ||
        ev.message?.includes('who is on stage') ||
        !!ev.payloadJson?.includes('"kind":"roster"'))
    );
  }

  protected isSystem(ev: ConsoleEvent): boolean {
    return !this.isUserMessage(ev) && !this.isSpeech(ev) && !this.isRosterBriefing(ev);
  }

  protected speakerFromPayload(ev: ConsoleEvent): string | null {
    if (!ev.payloadJson) return null;
    try {
      const p = JSON.parse(ev.payloadJson) as { displayName?: string };
      return p.displayName ?? null;
    } catch {
      return null;
    }
  }

  protected statusClass(status: string | undefined): string {
    return (status || 'Pending').toLowerCase();
  }

  /** First 8 chars of the session GUID for compact lists. */
  protected shortId(id: string): string {
    return (id || '').replace(/-/g, '').slice(0, 8);
  }

  protected async copySessionId(): Promise<void> {
    const id = this.session()?.id;
    if (!id) return;
    try {
      await navigator.clipboard.writeText(id);
    } catch {
      this.error.set(`Session id: ${id}`);
    }
  }

  protected async copySessionLink(): Promise<void> {
    const id = this.session()?.id;
    if (!id) return;
    const url = `${window.location.origin}/panel/${id}`;
    try {
      await navigator.clipboard.writeText(url);
    } catch {
      this.error.set(url);
    }
  }

  private loadSessionById(sessionId: string): void {
    this.pollSub?.unsubscribe();
    this.stream.reset();
    this.error.set(null);
    this.api.getPanel(sessionId).subscribe({
      next: async (detail) => {
        this.session.set(detail);
        this.topic.set(detail.topic);
        this.title.set(detail.title);
        this.selectedGroupId.set(detail.groupId);
        this.stream.setEvents(detail.consoleEvents ?? []);
        await this.stream.connect(detail.id);
        if (detail.status === 'Pending' || detail.status === 'Running') {
          this.startPolling(detail.id);
        }
        this.reloadRecent();
      },
      error: (err) => this.error.set(this.readError(err, 'Failed to load panel'))
    });
  }

  private startPolling(panelId: string): void {
    this.pollSub?.unsubscribe();
    this.pollSub = interval(2000)
      .pipe(
        switchMap(() => this.api.getPanel(panelId)),
        takeWhile((s) => s.status === 'Pending' || s.status === 'Running', true)
      )
      .subscribe({
        next: (detail) => {
          this.session.set(detail);
          // Keep Saved panels status in sync (Pending → Running → Completed).
          this.recent.update((list) =>
            list.map((p) =>
              p.id === detail.id
                ? {
                    ...p,
                    status: detail.status,
                    totalTokens: detail.totalTokens,
                    totalLatencyMs: detail.totalLatencyMs,
                    completedAt: detail.completedAt
                  }
                : p
            )
          );
          if (detail.status !== 'Pending' && detail.status !== 'Running') {
            this.api.getPanelEvents(panelId).subscribe({
              next: (ev) => this.stream.setEvents(ev)
            });
            this.reloadRecent();
          }
        }
      });
  }

  private reloadSession(panelId: string): void {
    this.api.getPanel(panelId).subscribe({
      next: (d) => this.session.set(d)
    });
  }
}
