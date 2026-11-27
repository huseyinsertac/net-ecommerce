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
import { Subscription, exhaustMap, takeUntil, takeWhile, tap, timer } from 'rxjs';
import {
  OrderDto,
  OrderingApi,
} from '../../api/ordering-api.service';
import { MoneyPipe } from '../../core/money.pipe';
import { RedirectService } from '../../core/redirect.service';
import { OrderTimelineComponent } from '../../shared/order-timeline/order-timeline.component';
import { OrderStatusChipComponent } from '../../shared/order-status-chip/order-status-chip.component';

const POLL_MS = 1000;
/** After `?checkout=success`: how long to wait for the payment to be confirmed. */
const GIVE_UP_MS = 30_000;
/** After `?checkout=cancelled`: a declined payment cancels the order a moment later. */
const CANCELLED_GIVE_UP_MS = 10_000;

/** `/orders/:id`: one order with its status timeline; also the return page after paying. */
@Component({
  selector: 'app-order-detail-page',
  imports: [
    DatePipe,
    MatButtonModule,
    MatIconModule,
    MoneyPipe,
    OrderStatusChipComponent,
    OrderTimelineComponent,
    RouterLink,
  ],
  templateUrl: './order-detail.page.html',
  styleUrl: './order-detail.page.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class OrderDetailPage implements OnInit {
  private readonly api = inject(OrderingApi);
  private readonly redirect = inject(RedirectService);

  /** Bound from the route (`withComponentInputBinding`). */
  readonly id = input.required<string>();
  /** `?checkout=success|cancelled`, set by the payment provider's return URL. */
  readonly checkout = input<string>();

  protected readonly view = signal<'loading' | 'ready' | 'notFound' | 'error'>('loading');
  protected readonly order = signal<OrderDto | null>(null);
  protected readonly confirming = signal(false);
  protected readonly payUrl = signal<string | null>(null);
  protected readonly skeleton = [1, 2, 3];

  private subs: Subscription[] = [];

  protected readonly paid = computed(() => {
    const status = this.order()?.status;
    return status === 'Paid' || status === 'Shipped' || status === 'Delivered';
  });

  protected readonly showPaidBanner = computed(() => this.checkout() === 'success' && this.paid());
  /**
   * The unpaid-order banner: always after `?checkout=cancelled` ("Payment not completed."), otherwise only when the
   * payment can be resumed ("This order is waiting for payment."). Never once the order has moved on.
   */
  protected readonly payBanner = computed<string | null>(() => {
    if (this.order()?.status !== 'Placed') return null;
    if (this.checkout() === 'cancelled') return 'Payment not completed.';
    if (this.checkout() !== 'success' && this.payUrl()) return 'This order is waiting for payment.';
    return null;
  });

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
          if (order.status === 'Placed') this.afterPlaced();
        },
        error: (err: unknown) =>
          this.view.set(err instanceof HttpErrorResponse && err.status === 404 ? 'notFound' : 'error'),
      }),
    );
  }

  protected payNow(): void {
    const url = this.payUrl();
    if (url) this.redirect.to(url);
  }

  private afterPlaced(): void {
    if (this.checkout() === 'success') {
      this.confirming.set(true);
      this.pollWhilePlaced(GIVE_UP_MS, () => this.confirming.set(false));
      return;
    }
    // Any other arrival (a cancelled checkout, My orders, a bookmark) may still have a payment to resume.
    this.loadPaymentUrl();
    if (this.checkout() === 'cancelled') this.pollWhilePlaced(CANCELLED_GIVE_UP_MS);
  }

  /** The outcome may land a moment after the redirect, so re-read the order until it moves on. */
  private pollWhilePlaced(giveUpMs: number, done?: () => void): void {
    const id = this.id();
    this.subs.push(
      timer(POLL_MS, POLL_MS)
        .pipe(
          exhaustMap(() => this.api.order(id)),
          tap((order) => this.order.set(order)),
          takeWhile((order) => order.status === 'Placed'),
          takeUntil(timer(giveUpMs)),
        )
        .subscribe({ error: () => done?.(), complete: () => done?.() }),
    );
  }

  private loadPaymentUrl(): void {
    this.subs.push(
      this.api.checkoutStatus(this.id()).subscribe({
        next: (status) =>
          this.payUrl.set(status.state === 'ReadyToPay' ? (status.paymentUrl ?? null) : null),
        error: () => this.payUrl.set(null),
      }),
    );
  }
}
