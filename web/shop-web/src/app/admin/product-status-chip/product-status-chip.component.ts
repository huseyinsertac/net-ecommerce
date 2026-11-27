import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import type { AdminProductStatus } from '../admin-api.service';

/** Pill for a product's lifecycle: Draft grey, Active green, Archived red. */
@Component({
  selector: 'app-product-status-chip',
  template: `<span class="chip" [class]="status().toLowerCase()">{{ status() }}</span>`,
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
    .draft {
      background: var(--mat-sys-surface-container-highest);
      color: var(--mat-sys-on-surface-variant);
    }
    .active {
      background: color-mix(in srgb, #15803d 16%, transparent);
      color: #166534;
    }
    .archived {
      background: var(--mat-sys-error-container);
      color: var(--mat-sys-on-error-container);
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ProductStatusChipComponent {
  readonly status = input.required<AdminProductStatus>();
}
