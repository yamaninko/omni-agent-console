import { Component, HostListener, inject } from '@angular/core';
import { DialogService } from './dialog.service';

@Component({
  selector: 'app-dialog-host',
  standalone: true,
  template: `
    @if (dialog.active(); as d) {
      <div class="dlg-backdrop" (click)="onBackdrop($event)" role="presentation">
        <div
          class="dlg-panel"
          role="dialog"
          aria-modal="true"
          [attr.aria-labelledby]="'dlg-title'"
          (click)="$event.stopPropagation()"
        >
          <h2 id="dlg-title" class="dlg-title">{{ d.title }}</h2>
          <p class="dlg-message">{{ d.message }}</p>
          <div class="dlg-actions">
            @if (d.kind === 'confirm' && d.cancelLabel) {
              <button type="button" class="dlg-btn secondary" (click)="dialog.close(false)">
                {{ d.cancelLabel }}
              </button>
            }
            <button
              type="button"
              class="dlg-btn"
              [class.primary]="!d.danger"
              [class.danger]="d.danger"
              (click)="dialog.close(true)"
            >
              {{ d.confirmLabel }}
            </button>
          </div>
        </div>
      </div>
    }
  `,
  styles: `
    .dlg-backdrop {
      position: fixed;
      inset: 0;
      z-index: 10000;
      display: flex;
      align-items: center;
      justify-content: center;
      padding: 24px;
      background: rgba(4, 8, 12, 0.72);
      backdrop-filter: blur(4px);
      animation: dlg-fade 0.15s ease-out;
    }

    .dlg-panel {
      width: min(420px, 100%);
      background: #0d131a;
      border: 1px solid #1b2a38;
      border-radius: 12px;
      box-shadow: 0 24px 64px rgba(0, 0, 0, 0.55);
      padding: 22px 22px 18px;
      animation: dlg-pop 0.16s ease-out;
    }

    .dlg-title {
      margin: 0 0 10px;
      font-size: 16px;
      font-weight: 700;
      color: #ffffff;
      letter-spacing: 0.01em;
    }

    .dlg-message {
      margin: 0 0 20px;
      font-size: 13.5px;
      line-height: 1.55;
      color: #a8b8c8;
      white-space: pre-wrap;
    }

    .dlg-actions {
      display: flex;
      justify-content: flex-end;
      gap: 8px;
      flex-wrap: wrap;
    }

    .dlg-btn {
      border-radius: 8px;
      border: 1px solid #1b2a38;
      background: #121a22;
      color: #e6edf3;
      font-size: 13px;
      font-weight: 600;
      padding: 8px 16px;
      cursor: pointer;
      min-height: 36px;
      transition: border-color 0.15s ease, background 0.15s ease, color 0.15s ease;

      &:hover {
        border-color: #2a3d52;
      }

      &.primary {
        background: #76b900;
        border-color: #76b900;
        color: #0b1017;

        &:hover {
          filter: brightness(1.06);
        }
      }

      &.danger {
        background: rgba(255, 59, 48, 0.16);
        border-color: #ff3b30;
        color: #ff7b72;

        &:hover {
          background: rgba(255, 59, 48, 0.28);
        }
      }

      &.secondary {
        background: transparent;
        color: #8fa3b7;
      }
    }

    @keyframes dlg-fade {
      from {
        opacity: 0;
      }
      to {
        opacity: 1;
      }
    }

    @keyframes dlg-pop {
      from {
        opacity: 0;
        transform: translateY(8px) scale(0.98);
      }
      to {
        opacity: 1;
        transform: translateY(0) scale(1);
      }
    }
  `
})
export class DialogHostComponent {
  protected readonly dialog = inject(DialogService);

  @HostListener('document:keydown.escape')
  onEscape(): void {
    if (this.dialog.active()) {
      this.dialog.close(false);
    }
  }

  protected onBackdrop(event: MouseEvent): void {
    if (event.target === event.currentTarget) {
      this.dialog.close(false);
    }
  }
}
