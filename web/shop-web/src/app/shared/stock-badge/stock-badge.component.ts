import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { stockLabel } from './stock-label';

@Component({
  selector: 'app-stock-badge',
  template: `@if (label(); as text) {
    <span class="badge" [class.out]="text === 'Out of stock'" [class.low]="text.startsWith('Only')">{{ text }}</span>
  }`,
  styles: `
    .badge {
      font: var(--mat-sys-label-medium);
      color: var(--mat-sys-tertiary);
    }
    .badge.low {
      color: #b45309; /* amber-700: 5:1 on white */
    }
    .badge.out {
      color: var(--mat-sys-error);
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class StockBadgeComponent {
  /** Units available (detail view). Takes precedence over `inStock` when both are set. */
  readonly available = input<number>();
  /** Flag only (list view, where the count is not known). */
  readonly inStock = input<boolean>();

  /** Renders nothing when neither input is provided. */
  protected readonly label = computed(() => {
    const n = this.available();
    if (n !== undefined) return stockLabel(n);
    const flag = this.inStock();
    if (flag === undefined) return undefined;
    return flag ? 'In stock' : stockLabel(0);
  });
}
