import { DatePipe } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  OnInit,
  computed,
  inject,
  input,
  signal,
} from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { RouterLink } from '@angular/router';
import { Observable, Subscription } from 'rxjs';
import { MoneyPipe } from '../../core/money.pipe';
import { OrderStatusChipComponent } from '../../shared/order-status-chip/order-status-chip.component';
import { OrderTimelineComponent } from '../../shared/order-timeline/order-timeline.component';
import { AdminApi, AdminOrderDto } from '../admin-api.service';

type Action = 'ship' | 'deliver';

/** `/admin/orders/:id`: one order for the admin, with the next fulfilment action. */
@Component({
  selector: 'app-admin-order-detail-page',
  imports: [
    DatePipe,
    MatButtonModule,
    MatIconModule,
    MoneyPipe,
    OrderStatusChipComponent,
    OrderTimelineComponent,
    RouterLink,
  ],
  templateUrl: './admin-order-detail.page.html',
  styleUrls: ['../../shared/order-view.scss', './admin-order-detail.page.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AdminOrderDetailPage implements OnInit {
  private readonly api = inject(AdminApi);

  /** Bound from the route. */
  readonly id = input.required<string>();

  protected readonly view = signal<'loading' | 'ready' | 'notFound' | 'error'>('loading');
  protected readonly order = signal<AdminOrderDto | null>(null);
  /** The action whose request is in flight. */
  protected readonly pending = signal<Action | null>(null);
  protected readonly actionError = signal<string | null>(null);
  protected readonly skeleton = [1, 2, 3];
  /** The one action that makes sense now: Paid orders ship, Shipped orders are delivered. */
  protected readonly nextAction = computed<Action | null>(() => {
    const status = this.order()?.status;
    return status === 'Paid' ? 'ship' : status === 'Shipped' ? 'deliver' : null;
  });

  private subs: Subscription[] = [];

  constructor() {
    inject(DestroyRef).onDestroy(() => this.subs.forEach((s) => s.unsubscribe()));
  }

  ngOnInit(): void {
    this.load();
  }

  protected load(): void {
    this.view.set('loading');
    this.subs.push(
      this.api.order(this.id()).subscribe({
        next: (order) => {
          this.order.set(order);
          this.view.set('ready');
        },
        error: (err: unknown) =>
          this.view.set(err instanceof HttpErrorResponse && err.status === 404 ? 'notFound' : 'error'),
      }),
    );
  }

  protected run(action: Action): void {
    if (this.pending()) return;
    const request: Observable<AdminOrderDto> =
      action === 'ship' ? this.api.ship(this.id()) : this.api.deliver(this.id());
    this.pending.set(action);
    this.actionError.set(null);
    this.subs.push(
      request.subscribe({
        next: (order) => {
          this.order.set(order);
          this.pending.set(null);
        },
        error: (err: unknown) => {
          this.pending.set(null);
          const title = err instanceof HttpErrorResponse ? (err.error as { title?: string } | null)?.title : null;
          this.actionError.set(title || 'That did not work. Please try again.');
          // The order moved on elsewhere: show where it is now.
          if (err instanceof HttpErrorResponse && err.status === 409) this.refresh();
        },
      }),
    );
  }

  /** Re-reads the order without flashing the loading state. */
  private refresh(): void {
    this.subs.push(this.api.order(this.id()).subscribe({ next: (order) => this.order.set(order) }));
  }
}
