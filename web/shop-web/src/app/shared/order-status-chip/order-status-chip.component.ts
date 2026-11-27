import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import type { OrderStatus } from '../../api/ordering-api.service';

type Tone = 'neutral' | 'primary' | 'success' | 'warn';

const TONES: Record<OrderStatus, Tone> = {
  Placed: 'neutral',
  Paid: 'primary',
  Shipped: 'success',
  Delivered: 'success',
  Cancelled: 'warn',
};

/** Pill showing an order's status; the colour carries meaning, the label carries the text. */
@Component({
  selector: 'app-order-status-chip',
  template: `<span class="chip" [class]="tone()">{{ status() }}</span>`,
  styles: `
    :host {
      display: inline-flex;
    }
    .chip {
      padding: 2px 12px;
      border-radius: 999px;
      font: var(--mat-sys-label-large);
      white-space: nowrap;
    }
    .neutral {
      background: var(--mat-sys-surface-container-highest);
      color: var(--mat-sys-on-surface-variant);
    }
    .primary {
      background: var(--mat-sys-primary-container);
      color: var(--mat-sys-on-primary-container);
    }
    .success {
      background: color-mix(in srgb, #15803d 16%, transparent);
      color: #166534;
    }
    .warn {
      background: var(--mat-sys-error-container);
      color: var(--mat-sys-on-error-container);
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class OrderStatusChipComponent {
  readonly status = input.required<OrderStatus>();
  protected readonly tone = computed(() => TONES[this.status()]);
}
