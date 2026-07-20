import { Injectable, signal } from '@angular/core';

export type DialogKind = 'confirm' | 'alert';

export interface DialogState {
  kind: DialogKind;
  title: string;
  message: string;
  confirmLabel: string;
  cancelLabel: string;
  danger: boolean;
}

export interface ConfirmOptions {
  title?: string;
  message: string;
  confirmLabel?: string;
  cancelLabel?: string;
  /** Red confirm button for destructive actions */
  danger?: boolean;
}

export interface AlertOptions {
  title?: string;
  message: string;
  confirmLabel?: string;
}

/**
 * App-themed modal dialogs replacing window.confirm / alert.
 */
@Injectable({ providedIn: 'root' })
export class DialogService {
  private readonly state = signal<DialogState | null>(null);
  private pending: ((value: boolean) => void) | null = null;

  /** Read-only view for the host component. */
  readonly active = this.state.asReadonly();

  confirm(options: ConfirmOptions): Promise<boolean> {
    return new Promise<boolean>((resolve) => {
      // Resolve any previous dialog as cancel so callers never hang.
      this.pending?.(false);
      this.pending = resolve;
      this.state.set({
        kind: 'confirm',
        title: options.title ?? 'Confirm',
        message: options.message,
        confirmLabel: options.confirmLabel ?? 'Confirm',
        cancelLabel: options.cancelLabel ?? 'Cancel',
        danger: options.danger ?? false
      });
    });
  }

  alert(options: AlertOptions): Promise<void> {
    return this.confirm({
      title: options.title ?? 'Notice',
      message: options.message,
      confirmLabel: options.confirmLabel ?? 'OK',
      cancelLabel: '',
      danger: false
    }).then(() => undefined);
  }

  /** Called by the host: true = confirm, false = cancel/dismiss. */
  close(result: boolean): void {
    this.state.set(null);
    const resolve = this.pending;
    this.pending = null;
    resolve?.(result);
  }
}
