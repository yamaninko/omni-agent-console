import { DatePipe } from '@angular/common';
import { Component, OnInit, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { TaskApiClient } from '../../core/api/task-api-client';
import { DashboardOverview } from '../../core/models';

@Component({
  selector: 'app-dashboard-page',
  imports: [DatePipe, RouterLink],
  templateUrl: './dashboard-page.html',
  styleUrl: './dashboard-page.scss'
})
export class DashboardPage implements OnInit {
  private readonly api = inject(TaskApiClient);
  protected readonly overview = signal<DashboardOverview | null>(null);
  protected readonly loading = signal(true);

  ngOnInit(): void {
    this.loadOverview();
  }

  protected loadOverview(): void {
    this.loading.set(true);
    this.api.getDashboardOverview().subscribe({
      next: (overview) => {
        this.overview.set(overview);
        this.loading.set(false);
      },
      // complete never fires on error; without this the page stays on the loader.
      error: () => {
        this.overview.set(null);
        this.loading.set(false);
      }
    });
  }

  protected formatDuration(ms: number): string {
    if (!ms) {
      return '0 ms';
    }

    return ms < 1000 ? `${ms} ms` : `${(ms / 1000).toFixed(1)} s`;
  }
}
