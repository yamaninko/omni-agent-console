import { DatePipe } from '@angular/common';
import { Component, OnInit, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { TaskApiClient } from '../../core/api/task-api-client';
import { I18nService } from '../../core/i18n/i18n.service';
import { DashboardOverview } from '../../core/models';

@Component({
  selector: 'app-dashboard-page',
  imports: [DatePipe, RouterLink],
  templateUrl: './dashboard-page.html',
  styleUrl: './dashboard-page.scss'
})
export class DashboardPage implements OnInit {
  private readonly api = inject(TaskApiClient);
  private readonly i18n = inject(I18nService);

  protected t(key: string): string {
    return this.i18n.t(key);
  }
  protected readonly overview = signal<DashboardOverview | null>(null);
  protected readonly loading = signal(true);
  protected readonly cancellingId = signal<string | null>(null);
  protected readonly actionError = signal<string | null>(null);

  ngOnInit(): void {
    this.loadOverview();
  }

  protected loadOverview(): void {
    this.loading.set(true);
    this.actionError.set(null);
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

  protected cancelLive(kind: string, id: string, event: Event): void {
    event.preventDefault();
    event.stopPropagation();
    if (this.cancellingId()) return;
    if (!confirm(`Cancel this ${kind}?`)) return;
    this.cancellingId.set(id);
    this.actionError.set(null);
    const req =
      kind === 'panel' ? this.api.cancelPanel(id) : this.api.cancelTask(id);
    req.subscribe({
      next: () => {
        this.cancellingId.set(null);
        this.loadOverview();
      },
      error: (err) => {
        this.cancellingId.set(null);
        this.actionError.set(err?.error || err?.message || 'Cancel failed');
      }
    });
  }

  protected formatDuration(ms: number): string {
    if (!ms) {
      return '0 ms';
    }

    return ms < 1000 ? `${ms} ms` : `${(ms / 1000).toFixed(1)} s`;
  }

  protected formatCost(usd: number | undefined): string {
    if (usd === undefined || usd === null || usd <= 0) return '$0';
    if (usd < 0.01) return `$${usd.toFixed(5)}`;
    return `$${usd.toFixed(4)}`;
  }

  protected shortSession(id: string): string {
    return (id || '').replace(/-/g, '').slice(0, 8);
  }
}
