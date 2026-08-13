import { Component, OnDestroy, OnInit, inject, signal, computed } from '@angular/core';
import { DomSanitizer, SafeResourceUrl } from '@angular/platform-browser';
import { Router } from '@angular/router';
import {
  LucideAngularModule,
  Folder,
  FolderOpen,
  FileCode,
  FileText,
  RefreshCw,
  Trash2,
  Play,
  Square,
  Copy,
  ExternalLink,
  Rocket,
  Send,
  ChevronsDown,
  ChevronsUp,
  Wrench
} from 'lucide-angular';
import { TaskApiClient } from '../../core/api/task-api-client';
import { I18nService } from '../../core/i18n/i18n.service';
import {
  ProjectDetectResponse,
  ProjectProxyResponse,
  ProjectRunStatusResponse,
  ProjectRouteHint,
  SkillDefinition,
  WorkspaceNode
} from '../../core/models';
import { DialogService } from '../../core/ui/dialog.service';

export interface FlatNode {
  name: string;
  path: string;
  isDirectory: boolean;
  level: number;
}

@Component({
  selector: 'app-workspace-page',
  imports: [LucideAngularModule],
  templateUrl: './workspace-page.html',
  styleUrl: './workspace-page.scss'
})
export class WorkspacePage implements OnInit, OnDestroy {
  private readonly api = inject(TaskApiClient);
  private readonly router = inject(Router);
  private readonly sanitizer = inject(DomSanitizer);
  private readonly dialog = inject(DialogService);
  private readonly i18n = inject(I18nService);

  protected t(key: string): string {
    return this.i18n.t(key);
  }

  protected readonly icons = {
    folder: Folder,
    folderOpen: FolderOpen,
    fileCode: FileCode,
    fileText: FileText,
    refresh: RefreshCw,
    trash: Trash2,
    play: Play,
    stop: Square,
    copy: Copy,
    external: ExternalLink,
    rocket: Rocket,
    send: Send,
    expandAll: ChevronsDown,
    collapseAll: ChevronsUp,
    wrench: Wrench
  };

  protected readonly files = signal<WorkspaceNode[]>([]);
  protected readonly selectedFilePath = signal<string | null>(null);
  protected readonly selectedFileContent = signal<string | null>(null);
  protected readonly loading = signal(false);
  protected readonly expandedFolders = signal<Set<string>>(new Set());

  protected readonly projectFocusPath = signal<string | null>(null);
  protected readonly projectInfo = signal<ProjectDetectResponse | null>(null);
  protected readonly projectStatus = signal<ProjectRunStatusResponse | null>(null);
  protected readonly projectBusy = signal(false);
  protected readonly projectMessage = signal<string | null>(null);
  protected readonly copyFlash = signal(false);
  protected readonly lastStartFailed = signal(false);
  protected readonly lastStartLog = signal<string | null>(null);
  protected readonly fixBusy = signal(false);
  private statusPoll?: ReturnType<typeof setInterval>;

  // API tester
  protected readonly reqMethod = signal('GET');
  protected readonly reqPath = signal('/health');
  protected readonly reqHeaders = signal('Content-Type: application/json');
  protected readonly reqBody = signal('{\n  "title": "demo",\n  "body": "hello"\n}');
  protected readonly reqSending = signal(false);
  protected readonly lastResponse = signal<ProjectProxyResponse | null>(null);

  protected readonly showApiTester = computed(() => {
    const kind = this.projectInfo()?.projectKind;
    return kind === 'api' || kind === 'hybrid' || kind === 'unknown';
  });

  protected readonly showOpenWeb = computed(() => {
    const kind = this.projectInfo()?.projectKind;
    return kind === 'web' || kind === 'hybrid';
  });

  /** In-page preview when a web stack is healthy/running. */
  protected readonly showWebPreview = computed(() => {
    if (!this.showOpenWeb()) {
      return false;
    }
    const st = this.projectStatus();
    return st?.state === 'running';
  });

  protected readonly previewUrl = computed(() => {
    return this.projectInfo()?.openUrl || this.projectStatus()?.healthUrl?.replace(/\/health$/, '/') || null;
  });

  protected readonly safePreviewUrl = computed((): SafeResourceUrl | null => {
    const url = this.previewUrl();
    if (!url || !/^https?:\/\/localhost(:\d+)?(\/|$)/i.test(url)) {
      return null;
    }
    return this.sanitizer.bypassSecurityTrustResourceUrl(url);
  });

  protected readonly methods = ['GET', 'POST', 'PUT', 'PATCH', 'DELETE'] as const;

  ngOnInit(): void {
    this.refreshFiles();
  }

  ngOnDestroy(): void {
    this.stopStatusPoll();
  }

  protected refreshFiles(): void {
    this.loading.set(true);
    this.api.getWorkspaceFiles().subscribe({
      next: (nodes) => {
        this.files.set(nodes);
        this.loading.set(false);
        // Default: collapsed. Keep only expansions that still exist after refresh.
        const valid = this.collectDirectoryPaths(nodes);
        const kept = new Set<string>();
        for (const path of this.expandedFolders()) {
          if (valid.has(path)) {
            kept.add(path);
          }
        }
        this.expandedFolders.set(kept);
      },
      error: () => {
        this.files.set([]);
        this.loading.set(false);
      }
    });
  }

  /** Expand every folder in the tree. */
  protected expandAllFolders(): void {
    this.expandedFolders.set(this.collectDirectoryPaths(this.files()));
  }

  /** Collapse the whole tree (projects closed by default look). */
  protected collapseAllFolders(): void {
    this.expandedFolders.set(new Set());
  }

  protected toggleFolder(path: string, event: Event): void {
    event.stopPropagation();
    const current = new Set(this.expandedFolders());
    if (current.has(path)) {
      current.delete(path);
    } else {
      current.add(path);
    }
    this.expandedFolders.set(current);
    this.selectProject(path);
  }

  protected isFolderExpanded(path: string): boolean {
    return this.expandedFolders().has(path);
  }

  protected selectFile(path: string): void {
    this.selectedFilePath.set(path);
    this.selectedFileContent.set('Loading file content...');
    this.selectProject(path);

    this.api.getWorkspaceFileContent(path).subscribe({
      next: (res) => this.selectedFileContent.set(res.content),
      error: () => this.selectedFileContent.set('Could not load file content or file is binary.')
    });
  }

  protected selectProject(path: string): void {
    this.projectFocusPath.set(path);
    this.projectMessage.set(null);
    this.api.detectWorkspaceProject(path).subscribe({
      next: (info) => {
        this.projectInfo.set(info);
        if (info.runnable) {
          this.refreshProjectStatus(info.projectRoot);
          this.startStatusPoll(info.projectRoot);
          // Seed path from first suggested route when switching projects.
          if (info.suggestedRoutes?.length) {
            const first = info.suggestedRoutes[0];
            this.reqMethod.set(first.method);
            this.reqPath.set(first.path);
          } else {
            this.reqPath.set('/health');
            this.reqMethod.set('GET');
          }
        } else {
          this.projectStatus.set(null);
          this.stopStatusPoll();
        }
      },
      error: () => {
        this.projectInfo.set(null);
        this.projectStatus.set(null);
      }
    });
  }

  protected startProject(): void {
    const info = this.projectInfo();
    if (!info?.runnable || this.projectBusy()) {
      return;
    }

    this.projectBusy.set(true);
    this.lastStartFailed.set(false);
    this.lastStartLog.set(null);
    const recreating = this.projectStatus()?.state === 'running';
    this.projectMessage.set(
      recreating
        ? 'Rebuilding and recreating containers… (may take 1–3 minutes)'
        : 'Building and starting… (may take 1–3 minutes)'
    );
    this.api.workspaceProjectUp(info.projectRoot).subscribe({
      next: (res) => {
        this.projectBusy.set(false);
        const log = res.message + (res.logsTail ? `\n\n${res.logsTail}` : '');
        this.projectMessage.set(log);
        if (!res.ok) {
          this.lastStartFailed.set(true);
          this.lastStartLog.set(log);
        } else {
          this.lastStartFailed.set(false);
          this.lastStartLog.set(null);
        }
        this.refreshProjectStatus(info.projectRoot);
        this.startStatusPoll(info.projectRoot);
      },
      error: (err) => {
        this.projectBusy.set(false);
        const body = err?.error;
        const log =
          typeof body?.message === 'string'
            ? body.message + (body.logsTail ? `\n\n${body.logsTail}` : '')
            : 'Failed to start project.';
        this.projectMessage.set(log);
        this.lastStartFailed.set(true);
        this.lastStartLog.set(log);
      }
    });
  }

  /**
   * Opens a new Studio task that asks the Coder to fix Docker packaging using
   * the last Start error log (e.g. missing package-lock.json COPY).
   */
  protected fixPackagingWithAi(): void {
    const info = this.projectInfo();
    const log = this.lastStartLog() || this.projectMessage();
    if (!info || !log || this.fixBusy()) {
      return;
    }

    this.fixBusy.set(true);
    this.projectMessage.set('Creating packaging fix task…');

    this.api.listSkills().subscribe({
      next: (skills) => {
        const skillIds = this.pickFixSkillIds(skills);
        const workspacePath = `/workspace/${info.projectRoot === '.' ? '' : info.projectRoot}`.replace(
          /\/$/,
          ''
        ) || '/workspace';
        const prompt = this.buildPackagingFixPrompt(info.projectRoot, log);
        const contextJson = JSON.stringify({ workspacePath, skillIds });

        this.api.createTask(prompt, contextJson).subscribe({
          next: (task) => {
            this.api.runTask(task.id).subscribe({
              complete: () => {
                this.fixBusy.set(false);
                this.lastStartFailed.set(false);
                void this.router.navigate(['/studio'], { queryParams: { task: task.id } });
              },
              error: () => {
                this.fixBusy.set(false);
                this.projectMessage.set(
                  `Fix task created (${task.id}) but run failed — open Studio and press Run.`
                );
                void this.router.navigate(['/studio'], { queryParams: { task: task.id } });
              }
            });
          },
          error: () => {
            this.fixBusy.set(false);
            this.projectMessage.set('Could not create packaging fix task.');
          }
        });
      },
      error: () => {
        this.fixBusy.set(false);
        this.projectMessage.set('Could not load skills for fix task.');
      }
    });
  }

  private pickFixSkillIds(skills: SkillDefinition[]): string[] {
    const want = ['Dockerized Service', 'Angular Frontend', 'React Frontend'];
    return skills
      .filter((s) => s.enabled && want.some((n) => s.name.includes(n.split(' ')[0]) || s.name === n))
      .map((s) => s.id)
      .slice(0, 4);
  }

  private buildPackagingFixPrompt(projectRoot: string, dockerLog: string): string {
    // Keep under console_events varchar(4000) when the orchestrator echoes the prompt;
    // prefer the tail of the log where Docker prints the actual error.
    const trimmedLog = dockerLog.length > 2800 ? dockerLog.slice(-2800) : dockerLog;
    return (
      `Workspace projesinin Docker packaging hatasını düzelt. Proje kökü: ${projectRoot}\n\n` +
      `SORUN: Workspace "Start (docker)" başarısız oldu. Aşağıdaki docker build/compose logunu oku ve ` +
      `yalnızca packaging dosyalarını (Dockerfile, docker-compose.yml, .dockerignore, nginx.conf, package.json, go.mod) ` +
      `düzelt — uygulama kodunu gereksiz yere yeniden yazma.\n\n` +
      `Yaygın kurallar:\n` +
      `- package-lock.json yoksa Dockerfile asla zorunlu COPY package-lock.json yapmasın; ` +
      `  \`COPY package.json package-lock.json* ./\` ve \`npm ci\` yoksa \`npm install\` kullan.\n` +
      `- Go: go.mod'ta gerçekten var olan module versiyonları kullan; Dockerfile \`go mod tidy\` ile build et.\n` +
      `- COPY ettiğin her dosya diskte olmalı (list_files ile doğrula).\n` +
      `- SPA: multi-stage node → nginx:alpine, /health 200, compose service adı app, ` +
      `  ports "\${HOST_PORT:-18080}:80", named volume tercih et, host bind mount kullanma.\n` +
      `- container_name sabitleme; obsolete compose version: koyma; Redis portunu host'a yayınlama.\n\n` +
      `DOCKER LOG:\n\`\`\`\n${trimmedLog}\n\`\`\`\n\n` +
      `write_file ile düzeltmeleri uygula; bitince list_files ile Dockerfile ve docker-compose.yml olduğunu doğrula.`
    );
  }

  protected stopProject(): void {
    const info = this.projectInfo();
    if (!info?.runnable || this.projectBusy()) {
      return;
    }

    this.projectBusy.set(true);
    this.api.workspaceProjectDown(info.projectRoot).subscribe({
      next: (res) => {
        this.projectBusy.set(false);
        this.projectMessage.set(res.message);
        this.refreshProjectStatus(info.projectRoot);
      },
      error: (err) => {
        this.projectBusy.set(false);
        this.projectMessage.set(err?.error?.message ?? 'Failed to stop project.');
      }
    });
  }

  protected copyUpCommand(): void {
    const cmd = this.projectInfo()?.upCommand;
    if (!cmd) {
      return;
    }

    void navigator.clipboard.writeText(cmd).then(() => {
      this.copyFlash.set(true);
      setTimeout(() => this.copyFlash.set(false), 1500);
    });
  }

  protected openHealth(): void {
    const url = this.projectInfo()?.healthUrl ?? this.projectStatus()?.healthUrl;
    if (url) {
      window.open(url, '_blank', 'noopener');
    }
  }

  protected openWeb(): void {
    const url = this.projectInfo()?.openUrl;
    if (url) {
      window.open(url, '_blank', 'noopener');
    }
  }

  /** Common Swagger UI paths produced by the Swagger / OpenAPI skill. */
  protected openSwagger(): void {
    const base = this.projectInfo()?.baseUrl?.replace(/\/$/, '');
    if (!base) {
      return;
    }
    // FastAPI default /docs; also try /swagger for .NET/Java conventions.
    window.open(`${base}/docs`, '_blank', 'noopener');
  }

  protected applyRoute(route: ProjectRouteHint): void {
    this.reqMethod.set(route.method);
    this.reqPath.set(route.path);
    if (route.exampleBody) {
      try {
        this.reqBody.set(JSON.stringify(JSON.parse(route.exampleBody), null, 2));
      } catch {
        this.reqBody.set(route.exampleBody);
      }
    }
  }

  protected setMethod(event: Event): void {
    this.reqMethod.set((event.target as HTMLSelectElement).value);
  }

  protected setPath(event: Event): void {
    this.reqPath.set((event.target as HTMLInputElement).value);
  }

  protected setHeaders(event: Event): void {
    this.reqHeaders.set((event.target as HTMLTextAreaElement).value);
  }

  protected setBody(event: Event): void {
    this.reqBody.set((event.target as HTMLTextAreaElement).value);
  }

  protected sendRequest(): void {
    const info = this.projectInfo();
    if (!info?.runnable) {
      return;
    }

    const path = this.reqPath().trim() || '/';
    const method = this.reqMethod();
    const headers = this.parseHeaders(this.reqHeaders());
    const body =
      method === 'GET' || method === 'HEAD' || method === 'DELETE'
        ? null
        : this.reqBody();

    this.reqSending.set(true);
    this.lastResponse.set(null);
    this.api
      .workspaceProjectProxy({
        projectPath: info.projectRoot,
        method,
        path,
        headers,
        body
      })
      .subscribe({
        next: (res) => {
          this.reqSending.set(false);
          this.lastResponse.set(res);
        },
        error: (err) => {
          this.reqSending.set(false);
          const bodyErr = err?.error;
          this.lastResponse.set({
            ok: false,
            statusCode: bodyErr?.statusCode ?? 0,
            latencyMs: bodyErr?.latencyMs ?? 0,
            contentType: bodyErr?.contentType,
            body: bodyErr?.body ?? '',
            headers: bodyErr?.headers ?? {},
            error: bodyErr?.error ?? err?.message ?? 'Request failed'
          });
        }
      });
  }

  protected prettyBody(res: ProjectProxyResponse): string {
    if (res.error && !res.body) {
      return res.error;
    }
    const raw = res.body ?? '';
    try {
      return JSON.stringify(JSON.parse(raw), null, 2);
    } catch {
      return raw;
    }
  }

  protected getFileExtension(name: string): string {
    const parts = name.split('.');
    return parts.length > 1 ? parts[parts.length - 1].toLowerCase() : '';
  }

  protected isCodeFile(name: string): boolean {
    const ext = this.getFileExtension(name);
    return ['go', 'js', 'ts', 'json', 'py', 'cs', 'html', 'css', 'yml', 'yaml', 'sh', 'md', 'dockerfile'].includes(ext);
  }

  protected getFlatNodes(): FlatNode[] {
    const list: FlatNode[] = [];
    const addNode = (node: WorkspaceNode, level: number, parentVisible: boolean) => {
      if (parentVisible) {
        list.push({
          name: node.name,
          path: node.path,
          isDirectory: node.isDirectory,
          level
        });
      }

      const expanded = this.isFolderExpanded(node.path);
      if (node.children) {
        for (const child of node.children) {
          addNode(child, level + 1, parentVisible && expanded);
        }
      }
    };

    for (const node of this.files()) {
      addNode(node, 0, true);
    }
    return list;
  }

  protected async deleteNode(node: FlatNode, event: Event): Promise<void> {
    event.stopPropagation();
    const ok = await this.dialog.confirm({
      title: node.isDirectory ? 'Delete folder' : 'Delete file',
      message: `Permanently delete "${node.name}"${node.isDirectory ? ' and all its contents' : ''}? This cannot be undone.`,
      confirmLabel: 'Delete',
      cancelLabel: 'Cancel',
      danger: true
    });
    if (!ok) {
      return;
    }

    this.api.deleteWorkspaceNode(node.path).subscribe({
      next: () => {
        if (this.selectedFilePath() === node.path) {
          this.selectedFilePath.set(null);
          this.selectedFileContent.set(null);
        }
        this.refreshFiles();
      }
    });
  }

  private parseHeaders(raw: string): Record<string, string> {
    const result: Record<string, string> = {};
    for (const line of raw.split('\n')) {
      const trimmed = line.trim();
      if (!trimmed || trimmed.startsWith('#')) {
        continue;
      }
      const idx = trimmed.indexOf(':');
      if (idx <= 0) {
        continue;
      }
      const key = trimmed.slice(0, idx).trim();
      const value = trimmed.slice(idx + 1).trim();
      if (key) {
        result[key] = value;
      }
    }
    return result;
  }

  private collectDirectoryPaths(nodes: WorkspaceNode[]): Set<string> {
    const paths = new Set<string>();
    const walk = (list: WorkspaceNode[]) => {
      for (const node of list) {
        if (node.isDirectory) {
          paths.add(node.path);
          if (node.children?.length) {
            walk(node.children);
          }
        }
      }
    };
    walk(nodes);
    return paths;
  }

  private refreshProjectStatus(projectRoot: string): void {
    this.api.workspaceProjectStatus(projectRoot).subscribe({
      next: (status) => this.projectStatus.set(status),
      error: () => this.projectStatus.set(null)
    });
  }

  private startStatusPoll(projectRoot: string): void {
    this.stopStatusPoll();
    // docker compose ps via the socket is expensive on Windows Docker Desktop;
    // 8s is enough for a status badge without constant process spawning.
    this.statusPoll = setInterval(() => this.refreshProjectStatus(projectRoot), 8000);
  }

  private stopStatusPoll(): void {
    if (this.statusPoll) {
      clearInterval(this.statusPoll);
      this.statusPoll = undefined;
    }
  }
}
