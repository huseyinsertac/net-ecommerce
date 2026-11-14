import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { stockLabel } from './stock-label';

@Component({
  selector: 'app-stock-badge',
  template: `<span class="badge" [class.out]="label() === 'Out of stock'">{{ label() }}</span>`,
  styles: `
    .badge {
      font: var(--mat-sys-label-medium);
      color: var(--mat-sys-tertiary);
    }
    .badge.out {
      color: var(--mat-sys-error);
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class StockBadgeComponent {
  /** Units available (detail view). */
  readonly available = input<number>();
  /** Flag only (list view, where the count is not known). */
  readonly inStock = input<boolean>();

  protected readonly label = computed(() => {
    const n = this.available();
    if (n !== undefined) return stockLabel(n);
    return this.inStock() === false ? stockLabel(0) : 'In stock';
  });
}
