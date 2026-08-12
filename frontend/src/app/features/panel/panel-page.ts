import { DatePipe } from '@angular/common';
import {
  Component,
  ElementRef,
  OnDestroy,
  OnInit,
  effect,
  inject,
  signal,
  viewChild
} from '@angular/core';
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
  private tickSub?: Subscription;
  private readonly messagesBox = viewChild<ElementRef<HTMLDivElement>>('messagesBox');
  private stickToBottom = true;
  /** Wall clock for floor progress ticks. */
  protected readonly nowMs = signal(Date.now());

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
  protected readonly maxRounds = signal(1);
  protected readonly followUp = signal('');
  protected readonly session = signal<PanelSessionDetail | null>(null);
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly apiKeyConfigured = signal<boolean | null>(null);
  /** conversation = speeches + topic + floor + roster; all = include model noise */
  protected readonly streamFilter = signal<'conversation' | 'all'>('conversation');

  protected readonly events = this.stream.events;

  protected readonly sampleTopics: Record<string, string[]> = {
    default: [
      'Should remote-first be the default for product engineering teams?',
      'Is open-source AI a net positive for society?',
      'Can multi-agent systems replace junior developers this decade?'
    ],
    anunnaki: [
      'Anunnakiler gerçek tarihsel varlıklar mıydı, yoksa yalnızca mit ve popüler kültür mü?',
      'Were the Anunnaki deities, rulers later deified, or modern fiction?',
      'Should ancient astronaut theories be taught as history or as folklore?'
    ],
    remote: [
      'Should companies permanently adopt remote-first policies?',
      'Does office presence improve mentorship more than remote tooling?',
      'Is hybrid work a stable compromise or the worst of both worlds?'
    ]
  };

  constructor() {
    // Auto-scroll when stream grows (unless user scrolled up).
    effect(() => {
      this.visibleEvents();
      this.session();
      this.nowMs();
      queueMicrotask(() => this.scrollMessagesIfPinned());
    });
  }

  ngOnInit(): void {
    this.tickSub = interval(1000).subscribe(() => this.nowMs.set(Date.now()));
    this.api.getSettings().subscribe({
      next: (s) => this.apiKeyConfigured.set(!!s.apiKeyConfigured),
      error: () => this.apiKeyConfigured.set(null)
    });
    this.api.listAgentGroups().subscribe({
      next: (g) => {
        this.groups.set(g);
        const fromQuery = this.route.snapshot.queryParamMap.get('groupId');
        if (fromQuery && g.some((x) => x.id === fromQuery)) {
          this.selectedGroupId.set(fromQuery);
        } else if (g.length && !this.selectedGroupId()) {
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
    this.tickSub?.unsubscribe();
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

    this.api
      .createPanel(groupId, topic, this.title().trim() || null, this.maxRounds())
      .subscribe({
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

  protected continuePanel(): void {
    const id = this.session()?.id;
    const message = this.followUp().trim();
    if (!id || !message) {
      this.error.set('Write a follow-up message for the panel.');
      return;
    }
    this.busy.set(true);
    this.error.set(null);
    this.api.continuePanel(id, message, 1).subscribe({
      next: async () => {
        this.busy.set(false);
        this.followUp.set('');
        await this.stream.connect(id);
        this.startPolling(id);
        this.reloadRecent();
      },
      error: (err) => {
        this.busy.set(false);
        this.error.set(this.readError(err, 'Continue failed'));
      }
    });
  }

  protected canContinue(): boolean {
    const s = this.session()?.status;
    return s === 'Completed' || s === 'Failed' || s === 'Cancelled';
  }

  protected downloadTranscript(): void {
    const id = this.session()?.id;
    if (!id) return;
    this.api.getPanelTranscript(id).subscribe({
      next: (md) => {
        const blob = new Blob([md], { type: 'text/markdown;charset=utf-8' });
        const url = URL.createObjectURL(blob);
        const a = document.createElement('a');
        a.href = url;
        a.download = `panel-${id}.md`;
        a.click();
        URL.revokeObjectURL(url);
      },
      error: (err) => this.error.set(this.readError(err, 'Transcript download failed'))
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

  protected visibleEvents(): ConsoleEvent[] {
    const all = this.events();
    if (this.streamFilter() === 'all') {
      return all;
    }
    // Conversation mode: topic, roster, floor grants, speeches, user follow-ups.
    // Hide model/agent plumbing noise (AgentStarted, Warning, ModelCall*, Usage…).
    return all.filter(
      (ev) =>
        this.isUserMessage(ev) ||
        this.isRosterBriefing(ev) ||
        this.isFloor(ev) ||
        this.isSpeech(ev) ||
        ev.eventType === 'PanelStarted' ||
        ev.eventType === 'PanelCompleted' ||
        ev.eventType === 'TaskCancelled' ||
        ev.eventType === 'TaskFailed'
    );
  }

  protected topicsForSelectedGroup(): string[] {
    const id = this.selectedGroupId();
    const g = this.groups().find((x) => x.id === id);
    const name = `${g?.name ?? ''} ${g?.description ?? ''}`.toLowerCase();
    if (name.includes('anunnak') || name.includes('annunak') || name.includes('sumer')) {
      return this.sampleTopics['anunnaki'];
    }
    if (name.includes('remote') || name.includes('office') || name.includes('work')) {
      return this.sampleTopics['remote'];
    }
    return this.sampleTopics['default'];
  }

  protected applyTopic(topic: string): void {
    this.topic.set(topic);
    if (!this.title().trim()) {
      this.title.set(topic.length > 60 ? topic.slice(0, 57) + '…' : topic);
    }
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

  /** Name of the speaker currently holding the floor (Running turn). */
  protected speakingName(): string | null {
    const s = this.session();
    if (!s || (s.status !== 'Running' && s.status !== 'Pending')) {
      return null;
    }
    const running = [...(s.turns ?? [])]
      .reverse()
      .find((t) => t.status === 'Running');
    if (running?.memberDisplayName) {
      return running.memberDisplayName;
    }
    if (s.currentMemberId) {
      return 'Guest';
    }
    return s.status === 'Running' || s.status === 'Pending' ? '…' : null;
  }

  protected floorSecondsLeft(): number | null {
    const deadline = this.session()?.floorDeadline;
    if (!deadline) return null;
    this.nowMs(); // re-evaluate every tick
    const ms = new Date(deadline).getTime() - Date.now();
    if (Number.isNaN(ms)) return null;
    return Math.max(0, Math.ceil(ms / 1000));
  }

  /** 0–100 remaining floor budget (bar width). */
  protected floorProgressPercent(): number {
    const left = this.floorSecondsLeft();
    if (left === null) return 0;
    const total = this.floorTotalSeconds();
    if (total <= 0) return 0;
    return Math.max(0, Math.min(100, Math.round((left / total) * 100)));
  }

  private floorTotalSeconds(): number {
    // Prefer timeout from the latest floor-grant payload; fall back to 60.
    const floors = [...this.events()].reverse().filter((e) => e.eventType === 'PanelFloorGranted');
    for (const ev of floors) {
      if (!ev.payloadJson) continue;
      try {
        const p = JSON.parse(ev.payloadJson) as { timeoutSeconds?: number };
        if (p.timeoutSeconds && p.timeoutSeconds > 0) {
          return p.timeoutSeconds;
        }
      } catch {
        /* ignore */
      }
    }
    return 60;
  }

  protected async deletePanel(panelId?: string): Promise<void> {
    const id = panelId || this.session()?.id;
    if (!id) return;
    if (!confirm('Delete this panel session and its transcript? This cannot be undone.')) {
      return;
    }
    this.api.deletePanel(id).subscribe({
      next: () => {
        if (this.session()?.id === id) {
          this.newSessionForm();
        }
        this.reloadRecent();
      },
      error: (err) => this.error.set(this.readError(err, 'Delete failed'))
    });
  }

  protected onMessagesScroll(): void {
    const el = this.messagesBox()?.nativeElement;
    if (!el) return;
    const distance = el.scrollHeight - el.scrollTop - el.clientHeight;
    this.stickToBottom = distance < 80;
  }

  private scrollMessagesIfPinned(): void {
    if (!this.stickToBottom) return;
    const el = this.messagesBox()?.nativeElement;
    if (!el) return;
    el.scrollTop = el.scrollHeight;
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
          // Refresh events while running so speeches appear without waiting for finish.
          this.api.getPanelEvents(panelId).subscribe({
            next: (ev) => this.stream.setEvents(ev)
          });
          if (detail.status !== 'Pending' && detail.status !== 'Running') {
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
