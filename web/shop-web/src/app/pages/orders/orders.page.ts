import { DatePipe } from '@angular/common';
import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  OnInit,
  inject,
  signal,
} from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatPaginatorModule, PageEvent } from '@angular/material/paginator';
import { RouterLink } from '@angular/router';
import { Subscription } from 'rxjs';
import { OrderSummaryPage, OrderingApi } from '../../api/ordering-api.service';
import { MoneyPipe } from '../../core/money.pipe';
import { OrderStatusChipComponent } from '../../shared/order-status-chip/order-status-chip.component';

const PAGE_SIZE = 10;

/** `/orders`: the signed-in shopper's order history. */
@Component({
  selector: 'app-orders-page',
  imports: [
    DatePipe,
    MatButtonModule,
    MatIconModule,
    MatPaginatorModule,
    MoneyPipe,
    OrderStatusChipComponent,
    RouterLink,
  ],
  templateUrl: './orders.page.html',
  styleUrl: './orders.page.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class OrdersPage implements OnInit {
  private readonly api = inject(OrderingApi);

  protected readonly pageSize = PAGE_SIZE;
  protected readonly skeletonRows = [1, 2, 3];
  protected readonly view = signal<'loading' | 'error' | 'ready'>('loading');
  protected readonly result = signal<OrderSummaryPage | null>(null);
  private sub: Subscription | null = null;

  constructor() {
    inject(DestroyRef).onDestroy(() => this.sub?.unsubscribe());
  }

  ngOnInit(): void {
    this.load(1);
  }

  protected load(page: number): void {
    this.sub?.unsubscribe();
    this.view.set('loading');
    this.sub = this.api.orders(page, PAGE_SIZE).subscribe({
      next: (result) => {
        this.result.set(result);
        this.view.set('ready');
      },
      error: () => this.view.set('error'),
    });
  }

  protected onPage(event: PageEvent): void {
    this.load(event.pageIndex + 1);
    window.scrollTo({ top: 0 });
  }
}
