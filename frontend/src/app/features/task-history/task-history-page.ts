import { Component, OnInit, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { RouterLink } from '@angular/router';
import { TaskApiClient } from '../../core/api/task-api-client';
import { TaskSummary } from '../../core/models';

@Component({
  selector: 'app-task-history-page',
  imports: [DatePipe, RouterLink],
  templateUrl: './task-history-page.html',
  styleUrl: './task-history-page.scss'
})
export class TaskHistoryPage implements OnInit {
  private readonly api = inject(TaskApiClient);
  protected readonly tasks = signal<TaskSummary[]>([]);
  protected readonly loading = signal(true);
  protected readonly error = signal<string | null>(null);

  ngOnInit(): void {
    this.loadTasks();
  }

  protected loadTasks(): void {
    this.loading.set(true);
    this.error.set(null);
    this.api.listTasks().subscribe({
      next: (tasks) => {
        this.tasks.set(tasks);
        this.loading.set(false);
      },
      // On error the observable never completes, so loading must be cleared
      // here or the page stays stuck on "Loading task runs...".
      error: () => {
        this.tasks.set([]);
        this.loading.set(false);
        this.error.set('Task history could not be loaded (backend unreachable). It may be restarting — click Refresh.');
      }
    });
  }

  protected formatDuration(ms: number): string {
    if (!ms) {
      return '-';
    }

    return ms < 1000 ? `${ms} ms` : `${(ms / 1000).toFixed(1)} s`;
  }
}
