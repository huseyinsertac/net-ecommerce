import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { MatCardModule } from '@angular/material/card';
import { RouterLink } from '@angular/router';
import type { ProductSummary } from '../../api/catalog-api.service';
import { MoneyPipe } from '../../core/money.pipe';
import { StockBadgeComponent } from '../stock-badge/stock-badge.component';

@Component({
  selector: 'app-product-card',
  imports: [RouterLink, MatCardModule, MoneyPipe, StockBadgeComponent],
  templateUrl: './product-card.component.html',
  styleUrl: './product-card.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ProductCardComponent {
  readonly product = input.required<ProductSummary>();
  protected readonly isRange = computed(() => this.product().priceFrom !== this.product().priceTo);
}
