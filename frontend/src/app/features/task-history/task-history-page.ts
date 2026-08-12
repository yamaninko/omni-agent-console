import { Component, OnInit, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { RouterLink } from '@angular/router';
import { forkJoin, of } from 'rxjs';
import { catchError } from 'rxjs/operators';
import { TaskApiClient } from '../../core/api/task-api-client';
import { PanelSessionSummary, TaskSummary } from '../../core/models';

export type HistoryKind = 'task' | 'panel';

export interface HistoryRow {
  kind: HistoryKind;
  id: string;
  title: string;
  subtitle: string;
  status: string;
  createdAt: string;
  totalLatencyMs: number;
  totalTokens: number;
  estimatedCost?: number;
  link: string[];
}

@Component({
  selector: 'app-task-history-page',
  imports: [DatePipe, RouterLink],
  templateUrl: './task-history-page.html',
  styleUrl: './task-history-page.scss'
})
export class TaskHistoryPage implements OnInit {
  private readonly api = inject(TaskApiClient);
  protected readonly rows = signal<HistoryRow[]>([]);
  protected readonly loading = signal(true);
  protected readonly error = signal<string | null>(null);
  protected readonly filter = signal<'all' | HistoryKind>('all');

  ngOnInit(): void {
    this.load();
  }

  protected load(): void {
    this.loading.set(true);
    this.error.set(null);
    forkJoin({
      tasks: this.api.listTasks().pipe(catchError(() => of([] as TaskSummary[]))),
      panels: this.api.listPanels().pipe(catchError(() => of([] as PanelSessionSummary[])))
    }).subscribe({
      next: ({ tasks, panels }) => {
        const taskRows: HistoryRow[] = tasks.map((t) => ({
          kind: 'task' as const,
          id: t.id,
          title: t.title,
          subtitle: 'Studio task',
          status: t.status,
          createdAt: t.createdAt,
          totalLatencyMs: t.totalLatencyMs,
          totalTokens: t.totalTokens,
          estimatedCost: t.estimatedCost ?? 0,
          link: ['/tasks', t.id]
        }));
        const panelRows: HistoryRow[] = panels.map((p) => ({
          kind: 'panel' as const,
          id: p.id,
          title: p.title || p.topic,
          subtitle: `Panel · ${p.groupName}`,
          status: p.status,
          createdAt: p.createdAt,
          totalLatencyMs: p.totalLatencyMs,
          totalTokens: p.totalTokens,
          estimatedCost: undefined,
          link: ['/panel', p.id]
        }));
        const merged = [...taskRows, ...panelRows].sort(
          (a, b) => new Date(b.createdAt).getTime() - new Date(a.createdAt).getTime()
        );
        this.rows.set(merged);
        this.loading.set(false);
        if (tasks.length === 0 && panels.length === 0) {
          // both empty is fine; only show error if both streams failed would need flags
        }
      },
      error: () => {
        this.rows.set([]);
        this.loading.set(false);
        this.error.set('History could not be loaded (backend unreachable). Click Refresh.');
      }
    });
  }

  protected visibleRows(): HistoryRow[] {
    const f = this.filter();
    if (f === 'all') return this.rows();
    return this.rows().filter((r) => r.kind === f);
  }

  protected formatDuration(ms: number): string {
    if (!ms) {
      return '-';
    }

    return ms < 1000 ? `${ms} ms` : `${(ms / 1000).toFixed(1)} s`;
  }

  protected formatCost(usd: number | undefined): string {
    if (usd === undefined || usd === null) {
      return '—';
    }
    if (usd <= 0) {
      return '$0';
    }
    if (usd < 0.01) {
      return `$${usd.toFixed(5)}`;
    }
    return `$${usd.toFixed(4)}`;
  }
}
