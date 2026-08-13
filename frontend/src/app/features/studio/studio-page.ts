import { Component, OnDestroy, OnInit, computed, inject, signal, effect, ViewChild, ElementRef } from '@angular/core';
import { DatePipe } from '@angular/common';
import { Bot, CirclePlay, LucideAngularModule, RadioTower, Send, SquareTerminal, Trash2, Plus, History, FileText, RefreshCw, ChevronDown, ChevronRight, Pencil, CheckCircle, AlertCircle, XCircle, Loader } from 'lucide-angular';
import { Router, ActivatedRoute } from '@angular/router';
import { TaskApiClient } from '../../core/api/task-api-client';
import { ConsoleStreamService } from '../../core/realtime/console-stream.service';
import { DomSanitizer, SafeHtml } from '@angular/platform-browser';
import { ConsoleEvent, RuntimeAgent, UsageSummary, TaskDetail, TaskSummary, SkillDefinition } from '../../core/models';
import { DialogService } from '../../core/ui/dialog.service';
import {
  DebouncedAction,
  SKILL_SUGGEST_DEBOUNCE_MS,
  shouldRequestSkillSuggestions
} from './debounced-action';
import { applySkillToggle, isAutoSuggestedSkill, mergeSelectedSkillIds } from './skill-selection';
import {
  beginContinue,
  beginCreateOrRerun,
  onCancelAccepted,
  onCancelError,
  onContinueTaskError,
  onCreateTaskError,
  onRunTaskAccepted,
  onRunTaskError,
  onStatusPollError,
  onTaskTerminalStatus
} from './studio-run-state';

@Component({
  selector: 'app-studio-page',
  imports: [DatePipe, LucideAngularModule],
  templateUrl: './studio-page.html',
  styleUrl: './studio-page.scss'
})
export class StudioPage implements OnInit, OnDestroy {
  private readonly api = inject(TaskApiClient);
  private readonly consoleStream = inject(ConsoleStreamService);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly sanitizer = inject(DomSanitizer);
  private readonly dialog = inject(DialogService);

  protected readonly icons = {
    bot: Bot,
    play: CirclePlay,
    realtime: RadioTower,
    send: Send,
    terminal: SquareTerminal,
    trash: Trash2,
    plus: Plus,
    history: History,
    file: FileText,
    refresh: RefreshCw,
    chevronDown: ChevronDown,
    chevronRight: ChevronRight,
    edit: Pencil,
    checkCircle: CheckCircle,
    alertCircle: AlertCircle,
    xCircle: XCircle,
    loader: Loader
  };

  protected readonly prompt = signal('');
  protected readonly followUpPrompt = signal('');
  protected readonly promptPlaceholder = 'Bu API dokumanina gore client SDK tasarla.';
  protected readonly followUpPlaceholder = 'Devam et: örn. login sayfasına şifre sıfırlama ekle…';
  protected readonly workspacePath = signal('/workspace/proje');
  /**
   * Studio agent chain: full | coder | plan-code-review
   * Stored in InputContextJson.pipeline and resolved by TaskPipelinePolicy.
   */
  protected readonly pipeline = signal<'full' | 'coder' | 'plan-code-review'>('full');
  protected readonly pipelineOptions = [
    {
      value: 'full' as const,
      label: 'Full chain',
      hint: 'Planner → Research → Coder → Reviewer → Ops'
    },
    {
      value: 'coder' as const,
      label: 'Coder only',
      hint: 'Skip planning/research — tool-loop write'
    },
    {
      value: 'plan-code-review' as const,
      label: 'Plan · Code · Review',
      hint: 'No Research or Ops Monitor'
    }
  ];
  /** Soft USD budget (0 = unlimited). Stored in InputContextJson.maxCostUsd. */
  protected readonly maxCostUsd = signal(0);
  protected readonly studioPresets = [
    { id: 'fastapi-notes', label: 'FastAPI notes' },
    { id: 'dotnet-api', label: '.NET API' },
    { id: 'angular-dashboard', label: 'Angular dash' }
  ];
  protected readonly skills = signal<SkillDefinition[]>([]);
  /** Skills grouped by category for the Studio chip panel (Frontend first). */
  protected readonly skillsByCategory = computed(() => {
    const order = ['Frontend', 'Backend', 'Packaging', 'Data', 'Security', 'Quality'];
    const groups = new Map<string, SkillDefinition[]>();
    for (const skill of this.skills()) {
      const list = groups.get(skill.category) ?? [];
      list.push(skill);
      groups.set(skill.category, list);
    }
    const ranked = order
      .filter((cat) => groups.has(cat))
      .map((category) => ({
        category,
        skills: (groups.get(category) ?? []).slice().sort((a, b) => a.name.localeCompare(b.name))
      }));
    for (const [category, list] of groups) {
      if (!order.includes(category)) {
        ranked.push({
          category,
          skills: list.slice().sort((a, b) => a.name.localeCompare(b.name))
        });
      }
    }
    return ranked;
  });
  private readonly manualSkillIds = signal<string[]>([]);
  private readonly dismissedAutoIds = signal<string[]>([]);
  protected readonly autoSuggestedIds = signal<string[]>([]);
  protected readonly suggestQuestions = signal<string[]>([]);
  protected readonly skillInfo = signal<SkillDefinition | null>(null);
  private readonly skillSuggestDebounce = new DebouncedAction(
    SKILL_SUGGEST_DEBOUNCE_MS,
    (value) => this.fetchSkillSuggestions(value)
  );

  // Effective selection = manual picks + prompt-based suggestions the user hasn't dismissed.
  protected readonly selectedSkillIds = computed(() =>
    mergeSelectedSkillIds(this.manualSkillIds(), this.autoSuggestedIds(), this.dismissedAutoIds())
  );
  protected readonly showRecentTasks = signal(true);
  protected readonly showAgents = signal(false);
  protected readonly showActiveTaskCollapse = signal(true);
  protected readonly showUsageCollapse = signal(true);
  protected readonly activeTaskId = signal<string | null>(null);
  protected readonly activeTask = signal<TaskDetail | null>(null);
  protected readonly agents = signal<RuntimeAgent[]>([]);
  protected readonly recentTasks = signal<TaskSummary[]>([]);
  protected readonly usage = signal<UsageSummary | null>(null);
  protected readonly pending = signal(false);
  protected readonly running = signal(false);
  protected readonly consoleEvents = computed<ConsoleEvent[]>(() => this.consoleStream.events());
  @ViewChild('terminalEl') private terminalEl?: ElementRef<HTMLDivElement>;
  private userScrolledUp = false;
  private statusPoll?: ReturnType<typeof setInterval>;
  /** Cache markdown → SafeHtml so console re-renders (SignalR / polls) do not re-parse large agent outputs. */
  private readonly markdownCache = new Map<string, SafeHtml>();
  private readonly markdownCacheOrder: string[] = [];
  private static readonly MaxMarkdownCacheEntries = 64;

  constructor() {
    effect(() => {
      const events = this.consoleEvents();
      if (events.length > 0) {
        this.scrollToBottomIfNeeded();
      }
    });
  }

  ngOnInit(): void {
    let savedPath = localStorage.getItem('studio_workspace_path');
    if (savedPath) {
      if (!savedPath.startsWith('/workspace/')) {
        const cleanSub = savedPath.replace(/^\/+|\/+$/g, '').split('/').pop() || 'proje';
        savedPath = `/workspace/${cleanSub}`;
        localStorage.setItem('studio_workspace_path', savedPath);
      }
      this.workspacePath.set(savedPath);
    }
    this.api.listRuntimeAgents().subscribe({
      next: (agents) => this.agents.set(agents),
      error: () => this.agents.set(this.fallbackAgents())
    });

    this.api.listSkills().subscribe({
      next: (skills) => {
        this.skills.set(skills.filter(s => s.enabled));
        const saved = localStorage.getItem('studio_selected_skills');
        if (saved) {
          try {
            const ids: string[] = JSON.parse(saved);
            this.manualSkillIds.set(ids.filter(id => skills.some(s => s.id === id && s.enabled)));
          } catch { }
        }
      },
      error: () => this.skills.set([])
    });

    this.loadRecentTasks();

    // Load task or demo preset from URL query params.
    this.route.queryParams.subscribe((params) => {
      const taskId = params['task'];
      if (taskId) {
        if (taskId !== this.activeTaskId()) {
          this.selectRecentTask(taskId, false);
        }
        return;
      }

      const pipeline = params['pipeline'] as 'full' | 'coder' | 'plan-code-review' | undefined;
      if (pipeline === 'full' || pipeline === 'coder' || pipeline === 'plan-code-review') {
        this.pipeline.set(pipeline);
      }
      if (typeof params['workspace'] === 'string' && params['workspace'].startsWith('/workspace/')) {
        this.workspacePath.set(params['workspace']);
        localStorage.setItem('studio_workspace_path', params['workspace']);
      }
      if (typeof params['prompt'] === 'string' && params['prompt'].trim()) {
        this.prompt.set(params['prompt']);
      }
      if (params['preset']) {
        // Skills may load async; retry once they arrive.
        const apply = () => this.applyPresetSkillKeywords(String(params['preset']));
        if (this.skills().length > 0) apply();
        else {
          const sub = this.api.listSkills().subscribe({
            next: (skills) => {
              this.skills.set(skills.filter((s) => s.enabled));
              apply();
              sub.unsubscribe();
            }
          });
        }
      }
    });

    this.api.getUsageSummary().subscribe({
      next: (usage) => this.usage.set(usage),
      error: () => this.usage.set(null)
    });
  }

  ngOnDestroy(): void {
    this.stopStatusPolling();
    this.skillSuggestDebounce.cancel();
    this.markdownCache.clear();
    this.markdownCacheOrder.length = 0;
  }

  protected applyStudioPreset(presetId: string): void {
    this.api.getStudioDemoPreset(presetId).subscribe({
      next: (p) => {
        const pipe = p.pipeline as 'full' | 'coder' | 'plan-code-review';
        if (pipe === 'full' || pipe === 'coder' || pipe === 'plan-code-review') {
          this.pipeline.set(pipe);
        }
        if (p.workspacePath?.startsWith('/workspace/')) {
          this.workspacePath.set(p.workspacePath);
          localStorage.setItem('studio_workspace_path', p.workspacePath);
        }
        if (p.prompt?.trim()) {
          this.prompt.set(p.prompt);
        }
        this.applyPresetSkillKeywords(p.id);
      },
      error: () => this.applyPresetSkillKeywords(presetId)
    });
  }

  /** Match skill chips by keyword for demo presets (best-effort). */
  private applyPresetSkillKeywords(presetId: string): void {
    const keywordMap: Record<string, string[]> = {
      'fastapi-notes': ['fastapi', 'python', 'test', 'docker', 'readme', 'health'],
      'dotnet-api': ['.net', 'asp', 'docker', 'readme', 'health', 'c#'],
      'angular-dashboard': ['angular', 'readme', 'frontend']
    };
    const keys = keywordMap[presetId] ?? [];
    if (!keys.length) return;
    const matched = this.skills()
      .filter((s) => {
        const hay = `${s.name} ${s.keywords ?? ''} ${s.category}`.toLowerCase();
        return keys.some((k) => hay.includes(k));
      })
      .map((s) => s.id);
    if (matched.length) {
      this.manualSkillIds.set(matched);
    }
  }

  protected startTask(): void {
    const prompt = this.prompt().trim();
    const workspace = this.workspacePath().trim();
    if (!prompt || !workspace || this.pending() || this.running()) {
      return;
    }

    this.applyRunFlags(beginCreateOrRerun());
    this.userScrolledUp = false;
    this.activeTask.set(null);
    this.consoleStream.reset();
    this.stopStatusPolling();

    const skillIds = this.selectedSkillIds();
    const context: Record<string, unknown> = {
      workspacePath: workspace,
      pipeline: this.pipeline()
    };
    if (skillIds.length > 0) {
      context['skillIds'] = skillIds;
    }
    const budget = this.maxCostUsd();
    if (budget > 0) {
      context['maxCostUsd'] = budget;
    }
    const contextJson = JSON.stringify(context);

    this.api.createTask(prompt, contextJson).subscribe({
      next: (task) => {
        this.prompt.set('');
        this.autoSuggestedIds.set([]);
        this.dismissedAutoIds.set([]);
        this.suggestQuestions.set([]);
        this.skillInfo.set(null);
        this.activeTaskId.set(task.id);
        // Lightweight shell so status poll / metrics work without a full GetById.
        this.activeTask.set({
          id: task.id,
          title: task.title,
          inputPrompt: prompt,
          status: task.status,
          createdAt: task.createdAt,
          completedAt: task.completedAt,
          totalInputTokens: 0,
          totalOutputTokens: 0,
          totalTokens: task.totalTokens ?? 0,
          totalLatencyMs: task.totalLatencyMs ?? 0,
          errorMessage: null,
          agentRuns: [],
          modelCallLogs: [],
          consoleEvents: []
        });
        this.router.navigate([], { queryParams: { task: task.id }, queryParamsHandling: 'merge' });
        this.loadRecentTasks();

        // Queue the run first — never block dispatch on SignalR connect.
        // (A failed/hanging hub handshake previously left tasks stuck in Pending
        // with only "Task created" and no worker message.)
        this.api.runTask(task.id).subscribe({
          complete: () => {
            this.applyRunFlags(onRunTaskAccepted());
            this.startStatusPolling(task.id);
            this.loadUsage();
          },
          error: () => {
            this.applyRunFlags(onRunTaskError());
            this.loadUsage();
          }
        });

        void this.consoleStream.connect(task.id).then(() => {
          this.api.getTaskEvents(task.id).subscribe({
            next: (events) => this.consoleStream.setEvents(events),
            error: () => { }
          });
        }).catch(() => {
          // Live stream optional; status polling still refreshes the console.
          this.api.getTaskEvents(task.id).subscribe({
            next: (events) => this.consoleStream.setEvents(events),
            error: () => { }
          });
        });
      },
      error: () => this.applyRunFlags(onCreateTaskError())
    });
  }

  protected updatePrompt(event: Event): void {
    const value = (event.target as HTMLTextAreaElement).value;
    this.prompt.set(value);
    this.skillSuggestDebounce.schedule(value);
  }

  protected updateFollowUpPrompt(event: Event): void {
    this.followUpPrompt.set((event.target as HTMLTextAreaElement).value);
  }

  protected onFollowUpKeydown(event: KeyboardEvent): void {
    if (event.key === 'Enter' && (event.ctrlKey || event.metaKey)) {
      event.preventDefault();
      this.continueActiveTask();
    }
  }

  protected continueActiveTask(): void {
    const taskId = this.activeTaskId();
    const prompt = this.followUpPrompt().trim();
    if (!taskId || !prompt || this.pending() || this.running()) {
      return;
    }

    this.applyRunFlags(beginContinue());
    this.userScrolledUp = false;
    this.stopStatusPolling();
    // Keep console history — continue must not wipe prior turns.

    this.api.continueTask(taskId, prompt).subscribe({
      complete: () => {
        this.followUpPrompt.set('');
        this.applyRunFlags(onRunTaskAccepted());
        this.startStatusPolling(taskId);
        this.loadUsage();
        this.loadRecentTasks();

        void this.consoleStream.connect(taskId).then(() => {
          this.api.getTaskEvents(taskId).subscribe({
            next: (events) => this.consoleStream.setEvents(events),
            error: () => { }
          });
        }).catch(() => {
          this.api.getTaskEvents(taskId).subscribe({
            next: (events) => this.consoleStream.setEvents(events),
            error: () => { }
          });
        });
      },
      error: () => {
        this.applyRunFlags(onContinueTaskError());
        this.loadUsage();
      }
    });
  }

  private fetchSkillSuggestions(prompt: string): void {
    const trimmed = prompt.trim();
    if (!shouldRequestSkillSuggestions(trimmed)) {
      this.autoSuggestedIds.set([]);
      this.suggestQuestions.set([]);
      return;
    }

    this.api.suggestSkills(trimmed).subscribe({
      next: (result) => {
        this.autoSuggestedIds.set(result.skillIds);
        this.suggestQuestions.set(result.questions);
      },
      error: () => { }
    });
  }

  protected toggleSkill(id: string): void {
    this.skillInfo.set(this.skills().find(s => s.id === id) ?? null);

    const next = applySkillToggle(
      id,
      this.manualSkillIds(),
      this.autoSuggestedIds(),
      this.dismissedAutoIds()
    );
    this.persistManualSkills(next.manual);
    this.dismissedAutoIds.set(next.dismissed);
  }

  private persistManualSkills(ids: string[]): void {
    this.manualSkillIds.set(ids);
    localStorage.setItem('studio_selected_skills', JSON.stringify(ids));
  }

  protected isSkillSelected(id: string): boolean {
    return this.selectedSkillIds().includes(id);
  }

  protected isAutoSuggested(id: string): boolean {
    return isAutoSuggestedSkill(
      id,
      this.manualSkillIds(),
      this.autoSuggestedIds(),
      this.dismissedAutoIds()
    );
  }

  private applyRunFlags(flags: { pending: boolean; running: boolean }): void {
    this.pending.set(flags.pending);
    this.running.set(flags.running);
  }

  protected autoSuggestedCount(): number {
    return this.autoSuggestedIds().filter(id => this.isAutoSuggested(id)).length;
  }

  protected getWorkspaceSubfolder(): string {
    const path = this.workspacePath();
    if (path.startsWith('/workspace/')) {
      return path.substring('/workspace/'.length);
    }
    if (path.startsWith('/workspace')) {
      return path.substring('/workspace'.length);
    }
    return path;
  }

  protected updateWorkspaceSubfolder(event: Event): void {
    const subfolder = (event.target as HTMLInputElement).value;
    const cleanSub = subfolder.replace(/^\/+|\/+$/g, '');
    const fullPath = subfolder.trim() ? `/workspace/${cleanSub}` : '';
    this.workspacePath.set(fullPath);
    localStorage.setItem('studio_workspace_path', fullPath);
  }

  protected loadRecentTasks(): void {
    this.api.listTasks().subscribe({
      next: (tasks) => this.recentTasks.set(tasks.slice(0, 8)),
      error: () => this.recentTasks.set([])
    });
  }

  protected selectRecentTask(taskId: string, updateUrl = true): void {
    if (this.pending()) return;
    
    this.activeTaskId.set(taskId);
    this.userScrolledUp = false;
    this.stopStatusPolling();
    this.consoleStream.reset();

    if (updateUrl) {
      this.router.navigate([], { queryParams: { task: taskId }, queryParamsHandling: 'merge' });
    }

    this.api.getTask(taskId).subscribe({
      next: async (task) => {
        this.activeTask.set(task);
        this.prompt.set(task.inputPrompt);
        this.followUpPrompt.set('');
        this.consoleStream.setEvents(task.consoleEvents);
        
        if (task.status === 'Running' || task.status === 'Pending') {
          this.applyRunFlags(onRunTaskAccepted());
          await this.consoleStream.connect(taskId);
          this.startStatusPolling(taskId);
        } else {
          this.applyRunFlags(onTaskTerminalStatus());
        }
      }
    });
  }

  protected startNewTask(): void {
    if (this.pending() || this.running()) return;
    this.activeTaskId.set(null);
    this.userScrolledUp = false;
    this.activeTask.set(null);
    this.prompt.set('');
    this.followUpPrompt.set('');
    this.consoleStream.reset();
    this.stopStatusPolling();
    this.router.navigate([], { queryParams: { task: null }, queryParamsHandling: 'merge' });
  }

  protected clearConsole(): void {
    this.consoleStream.setEvents([]);
    this.markdownCache.clear();
    this.markdownCacheOrder.length = 0;
  }

  /**
   * User turns arrive as UserMessage events. Tasks created before that event type
   * existed only have the orchestrator's prompt echo, so both are still accepted.
   */
  protected isUserMessage(event: ConsoleEvent): boolean {
    return (
      event.eventType === 'UserMessage' ||
      (event.eventType === 'TaskStarted' &&
        event.message.includes('Task execution started with prompt:'))
    );
  }

  protected getCleanPrompt(message: string): string {
    const prefix = 'Task execution started with prompt: ';
    if (!message.startsWith(prefix)) {
      return message;
    }

    const body = message.substring(prefix.length);
    return body.startsWith('"') && body.endsWith('"') ? body.slice(1, -1) : body;
  }

  protected getAgentOutput(payloadJson: string | null | undefined): string {
    if (!payloadJson) return '';
    try {
      const obj = JSON.parse(payloadJson);
      return obj.output || '';
    } catch {
      return '';
    }
  }

  protected getAgentName(payloadJson: string | null | undefined): string {
    if (!payloadJson) return 'Agent';
    try {
      const obj = JSON.parse(payloadJson);
      return obj.agentName || 'Agent';
    } catch {
      return 'Agent';
    }
  }

  protected parseMarkdown(text: string | null | undefined): SafeHtml {
    if (!text) return '';

    const cached = this.markdownCache.get(text);
    if (cached !== undefined) {
      return cached;
    }

    // Escape HTML first to prevent XSS
    let html = text
      .replace(/&/g, '&amp;')
      .replace(/</g, '&lt;')
      .replace(/>/g, '&gt;');

    // 1. Code blocks: ```language ... ```
    html = html.replace(/```(\w*)\n([\s\S]*?)```/g, (match, lang, code) => {
      const language = lang || 'code';
      return `
        <div class="code-block-container" style="background: #090e14; border: 1px solid #1a2636; border-radius: 6px; margin: 12px 0; overflow: hidden; font-family: ui-monospace, SFMono-Regular, SF Mono, Menlo, Monaco, Consolas, monospace; text-align: left;">
          <div class="code-block-header" style="background: #0d131a; padding: 6px 12px; font-size: 11px; color: #76b900; font-weight: 700; border-bottom: 1px solid #14212d; display: flex; justify-content: space-between; align-items: center; text-transform: uppercase;">
            <span>${language}</span>
          </div>
          <pre style="margin: 0; padding: 12px; overflow-x: auto; color: #e6edf3; font-size: 12.5px; line-height: 1.5; white-space: pre;"><code>${code}</code></pre>
        </div>
      `;
    });

    // 2. Headers: ## Title, ### Title, # Title
    html = html.replace(/^### (.*?)$/gm, '<h4 style="color: #76b900; font-size: 14px; margin: 16px 0 8px; font-weight: 700;">$1</h4>');
    html = html.replace(/^## (.*?)$/gm, '<h3 style="color: #ffffff; font-size: 16px; margin: 20px 0 10px; font-weight: 700; border-bottom: 1px solid #1a2636; padding-bottom: 4px;">$1</h3>');
    html = html.replace(/^# (.*?)$/gm, '<h2 style="color: #ffffff; font-size: 18px; margin: 24px 0 12px; font-weight: 800;">$1</h2>');

    // 3. Bold: **text**
    html = html.replace(/\*\*(.*?)\*\*/g, '<strong style="color: #ffffff; font-weight: 700;">$1</strong>');

    // 4. Inline code: `code`
    html = html.replace(/`(.*?)`/g, '<code style="background: #111a24; border: 1px solid #1a2532; padding: 2px 6px; border-radius: 4px; color: #76b900; font-family: inherit; font-size: 12px; font-weight: 600;">$1</code>');

    // 5. Bullet lists: - item or * item
    html = html.replace(/^[-\*]\s+(.*?)$/gm, '<li style="margin-left: 20px; margin-bottom: 4px; list-style-type: disc; color: #e6edf3;">$1</li>');

    // 6. Line breaks
    html = html.replace(/\n/g, '<br>');

    const safe = this.sanitizer.bypassSecurityTrustHtml(html);
    this.markdownCache.set(text, safe);
    this.markdownCacheOrder.push(text);
    while (this.markdownCacheOrder.length > StudioPage.MaxMarkdownCacheEntries) {
      const oldest = this.markdownCacheOrder.shift();
      if (oldest !== undefined) {
        this.markdownCache.delete(oldest);
      }
    }
    return safe;
  }

  protected getActiveTaskInputTokens(): number {
    return this.activeTask()?.totalInputTokens ?? 0;
  }

  protected getActiveTaskOutputTokens(): number {
    return this.activeTask()?.totalOutputTokens ?? 0;
  }

  protected getActiveTaskTotalTokens(): number {
    return this.activeTask()?.totalTokens ?? 0;
  }

  protected getActiveTaskLatency(): number {
    return this.activeTask()?.totalLatencyMs ?? 0;
  }

  protected toggleRecentTasks(): void {
    this.showRecentTasks.update(v => !v);
  }

  protected toggleAgents(): void {
    this.showAgents.update(v => !v);
  }

  protected toggleActiveTaskCollapse(): void {
    this.showActiveTaskCollapse.update(v => !v);
  }

  protected toggleUsageCollapse(): void {
    this.showUsageCollapse.update(v => !v);
  }

  protected cancelActiveTask(): void {
    const taskId = this.activeTaskId();
    if (!taskId || !this.running()) {
      return;
    }

    this.api.cancelTask(taskId).subscribe({
      complete: () => {
        this.applyRunFlags(onCancelAccepted());
        this.stopStatusPolling();
        this.loadUsage();
      },
      // Cancel request failed → the task is still running; keep the running
      // state so the user can retry instead of throwing an unhandled error.
      error: () => this.applyRunFlags(onCancelError({ pending: this.pending(), running: this.running() }))
    });
  }

  protected rerunActiveTask(): void {
    const taskId = this.activeTaskId();
    if (!taskId || this.pending() || this.running()) {
      return;
    }

    this.applyRunFlags(beginCreateOrRerun());
    this.userScrolledUp = false;
    const currentEvents = this.consoleStream.events();
    const promptEvent = currentEvents.find(e => e.eventType === 'TaskStarted');
    if (promptEvent) {
      this.consoleStream.setEvents([promptEvent]);
    } else {
      this.consoleStream.reset();
    }
    this.stopStatusPolling();

    this.api.runTask(taskId).subscribe({
      complete: () => {
        this.applyRunFlags(onRunTaskAccepted());
        this.startStatusPolling(taskId);
        this.loadUsage();
        this.loadRecentTasks();
      },
      error: () => {
        this.applyRunFlags(onRunTaskError());
        this.loadUsage();
      }
    });
  }

  protected getActiveWorkspacePath(): string {
    const task = this.activeTask();
    if (!task || !task.inputContextJson) {
      return '';
    }
    try {
      const context = JSON.parse(task.inputContextJson);
      return context.workspacePath || '';
    } catch {
      return '';
    }
  }

  protected sampleEvents(): ConsoleEvent[] {
    return [
      {
        id: 'sample-1',
        taskRunId: 'sample',
        eventType: 'TaskCreated',
        message: 'Console stream waiting for a task',
        createdAt: new Date().toISOString()
      },
      {
        id: 'sample-2',
        taskRunId: 'sample',
        eventType: 'AgentStep',
        message: 'Planner -> Research -> Coder -> Reviewer pipeline scaffold ready',
        createdAt: new Date().toISOString()
      }
    ];
  }

  private fallbackAgents(): RuntimeAgent[] {
    return [
      { name: 'Planner Agent', type: 'Planner', description: 'Execution planning' },
      { name: 'Research Agent', type: 'Research', description: 'Context analysis' },
      { name: 'Coder Agent', type: 'Coder', description: 'Technical output' },
      { name: 'Reviewer Agent', type: 'Reviewer', description: 'Quality review' },
      { name: 'Ops Monitor Agent', type: 'OpsMonitor', description: 'Usage telemetry' }
    ];
  }

  private loadUsage(): void {
    this.api.getUsageSummary().subscribe({
      next: (usage) => this.usage.set(usage),
      error: () => this.usage.set(null)
    });
  }

  private startStatusPolling(taskId: string): void {
    this.stopStatusPolling();

    // Light /status endpoint only — full GetById used to re-download agent I/O +
    // every console event every 2s and pinned Windows hosts under Docker Desktop.
    this.statusPoll = setInterval(() => {
      this.api.getTaskStatus(taskId).subscribe({
        next: (snap) => {
          this.activeTask.update((current) => {
            if (current && current.id === snap.id) {
              return {
                ...current,
                title: snap.title || current.title,
                status: snap.status,
                completedAt: snap.completedAt,
                totalInputTokens: snap.totalInputTokens,
                totalOutputTokens: snap.totalOutputTokens,
                totalTokens: snap.totalTokens,
                totalLatencyMs: snap.totalLatencyMs,
                errorMessage: snap.errorMessage
              };
            }
            return {
              id: snap.id,
              title: snap.title,
              inputPrompt: current?.inputPrompt ?? '',
              status: snap.status,
              createdAt: current?.createdAt ?? new Date().toISOString(),
              completedAt: snap.completedAt,
              totalInputTokens: snap.totalInputTokens,
              totalOutputTokens: snap.totalOutputTokens,
              totalTokens: snap.totalTokens,
              totalLatencyMs: snap.totalLatencyMs,
              errorMessage: snap.errorMessage,
              agentRuns: current?.agentRuns ?? [],
              modelCallLogs: current?.modelCallLogs ?? [],
              consoleEvents: current?.consoleEvents ?? []
            };
          });
          // Keep sidebar Recent Tasks in sync while running (status icon).
          this.recentTasks.update((list) =>
            list.map((t) =>
              t.id === snap.id
                ? {
                    ...t,
                    status: snap.status,
                    totalTokens: snap.totalTokens,
                    totalLatencyMs: snap.totalLatencyMs,
                    completedAt: snap.completedAt
                  }
                : t
            )
          );
          if (snap.status !== 'Running' && snap.status !== 'Pending') {
            this.applyRunFlags(onTaskTerminalStatus());
            this.stopStatusPolling();
            this.loadUsage();
            // One full fetch at terminal for final console + metrics graph.
            this.api.getTask(taskId).subscribe({
              next: (task) => {
                this.activeTask.set(task);
                this.consoleStream.setEvents(task.consoleEvents);
                this.loadRecentTasks();
              },
              error: () => this.loadRecentTasks()
            });
          }
        },
        error: () => {
          this.applyRunFlags(onStatusPollError());
          this.stopStatusPolling();
        }
      });
    }, 3000);
  }

  private stopStatusPolling(): void {
    if (!this.statusPoll) {
      return;
    }

    clearInterval(this.statusPoll);
    this.statusPoll = undefined;
  }

  protected onTerminalScroll(event: Event): void {
    const el = event.target as HTMLDivElement;
    if (!el) return;
    const isAtBottom = el.scrollHeight - el.scrollTop - el.clientHeight < 15;
    this.userScrolledUp = !isAtBottom;
  }

  protected editingTaskId = signal<string | null>(null);
  protected editingTitle = signal<string>('');

  protected startRename(taskId: string, currentTitle: string, event: Event): void {
    event.stopPropagation();
    this.editingTaskId.set(taskId);
    this.editingTitle.set(currentTitle);
    setTimeout(() => {
      const el = document.querySelector('input[type="text"][style*="background: #080d13"]') as HTMLInputElement;
      if (el) {
        el.focus();
        el.select();
      }
    }, 50);
  }

  protected updateEditingTitle(event: Event): void {
    this.editingTitle.set((event.target as HTMLInputElement).value);
  }

  protected saveRename(taskId: string): void {
    const newTitle = this.editingTitle().trim();
    if (!newTitle) {
      this.cancelRename();
      return;
    }

    this.api.renameTask(taskId, newTitle).subscribe({
      complete: () => {
        this.cancelRename();
        this.loadRecentTasks();
        const currentTask = this.activeTask();
        if (currentTask && currentTask.id === taskId) {
          this.activeTask.set({ ...currentTask, title: newTitle });
        }
      },
      // Rename failed → close the edit box and keep the original title.
      error: () => this.cancelRename()
    });
  }

  protected cancelRename(): void {
    this.editingTaskId.set(null);
    this.editingTitle.set('');
  }

  protected async deleteTask(taskId: string, event: Event): Promise<void> {
    event.stopPropagation();
    const ok = await this.dialog.confirm({
      title: 'Delete task',
      message: 'Are you sure you want to delete this task? This cannot be undone.',
      confirmLabel: 'Delete',
      cancelLabel: 'Cancel',
      danger: true
    });
    if (!ok) {
      return;
    }

    this.api.deleteTask(taskId).subscribe({
      complete: () => {
        this.loadRecentTasks();
        if (this.activeTaskId() === taskId) {
          this.startNewTask();
        }
      },
      // Delete failed → refresh the list so the UI reflects reality.
      error: () => this.loadRecentTasks()
    });
  }

  private scrollToBottomIfNeeded(): void {
    setTimeout(() => {
      if (!this.terminalEl) return;
      const el = this.terminalEl.nativeElement;
      if (!this.userScrolledUp) {
        el.scrollTop = el.scrollHeight;
      }
    }, 0);
  }
}
