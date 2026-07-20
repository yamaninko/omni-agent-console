import { Component, OnDestroy, OnInit, inject, signal } from '@angular/core';
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
  Rocket
} from 'lucide-angular';
import { TaskApiClient } from '../../core/api/task-api-client';
import { ProjectDetectResponse, ProjectRunStatusResponse, WorkspaceNode } from '../../core/models';

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
    rocket: Rocket
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
  private statusPoll?: ReturnType<typeof setInterval>;

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
        // Expand top-level folders by default for discoverability.
        const expanded = new Set(this.expandedFolders());
        for (const n of nodes) {
          if (n.isDirectory) {
            expanded.add(n.path);
          }
        }
        this.expandedFolders.set(expanded);
      },
      error: () => {
        this.files.set([]);
        this.loading.set(false);
      }
    });
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
    this.projectMessage.set('Building and starting… (may take 1–3 minutes)');
    this.api.workspaceProjectUp(info.projectRoot).subscribe({
      next: (res) => {
        this.projectBusy.set(false);
        this.projectMessage.set(res.message + (res.logsTail ? `\n\n${res.logsTail}` : ''));
        this.refreshProjectStatus(info.projectRoot);
        this.startStatusPoll(info.projectRoot);
      },
      error: (err) => {
        this.projectBusy.set(false);
        const body = err?.error;
        this.projectMessage.set(
          typeof body?.message === 'string'
            ? body.message + (body.logsTail ? `\n\n${body.logsTail}` : '')
            : 'Failed to start project.'
        );
      }
    });
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

  protected deleteNode(node: FlatNode, event: Event): void {
    event.stopPropagation();
    const confirmed = confirm(
      `Are you sure you want to permanently delete "${node.name}"${node.isDirectory ? ' and all its contents' : ''}?`
    );
    if (!confirmed) {
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

  private refreshProjectStatus(projectRoot: string): void {
    this.api.workspaceProjectStatus(projectRoot).subscribe({
      next: (status) => this.projectStatus.set(status),
      error: () => this.projectStatus.set(null)
    });
  }

  private startStatusPoll(projectRoot: string): void {
    this.stopStatusPoll();
    this.statusPoll = setInterval(() => this.refreshProjectStatus(projectRoot), 4000);
  }

  private stopStatusPoll(): void {
    if (this.statusPoll) {
      clearInterval(this.statusPoll);
      this.statusPoll = undefined;
    }
  }
}
