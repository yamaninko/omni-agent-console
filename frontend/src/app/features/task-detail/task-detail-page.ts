import { DatePipe } from '@angular/common';
import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { TaskApiClient } from '../../core/api/task-api-client';
import { AgentRunDetail, ModelCallLogDetail, TaskDetail } from '../../core/models';

@Component({
  selector: 'app-task-detail-page',
  imports: [DatePipe, RouterLink],
  templateUrl: './task-detail-page.html',
  styleUrl: './task-detail-page.scss'
})
export class TaskDetailPage implements OnInit {
  private readonly route = inject(ActivatedRoute);
  private readonly api = inject(TaskApiClient);

  protected readonly taskId = this.route.snapshot.paramMap.get('id');
  protected readonly task = signal<TaskDetail | null>(null);
  protected readonly loading = signal(true);
  protected readonly selectedAgentId = signal<string | null>(null);

  protected readonly selectedAgent = computed<AgentRunDetail | null>(() => {
    const task = this.task();
    if (!task?.agentRuns.length) {
      return null;
    }

    return task.agentRuns.find((agent) => agent.id === this.selectedAgentId()) ?? task.agentRuns[0];
  });

  ngOnInit(): void {
    this.loadTask();
  }

  protected loadTask(): void {
    if (!this.taskId) {
      this.loading.set(false);
      return;
    }

    this.loading.set(true);
    this.api.getTask(this.taskId).subscribe({
      next: (task) => {
        this.task.set(task);
        this.selectedAgentId.set(task.agentRuns[0]?.id ?? null);
        this.loading.set(false);
      },
      // complete never fires on error; without this the page stays on the loader.
      error: () => {
        this.task.set(null);
        this.loading.set(false);
      }
    });
  }

  protected selectAgent(agentId: string): void {
    this.selectedAgentId.set(agentId);
  }

  protected modelCallsFor(agentId: string | null | undefined): ModelCallLogDetail[] {
    if (!agentId) {
      return [];
    }

    return this.task()?.modelCallLogs.filter((call) => call.agentRunId === agentId) ?? [];
  }

  protected formatDuration(ms: number): string {
    if (!ms) {
      return '-';
    }

    return ms < 1000 ? `${ms} ms` : `${(ms / 1000).toFixed(1)} s`;
  }
}
