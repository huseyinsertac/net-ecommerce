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
  OrderStatus,
  OrderingApi,
} from '../../api/ordering-api.service';
import { MoneyPipe } from '../../core/money.pipe';
import { RedirectService } from '../../core/redirect.service';
import { OrderStatusChipComponent } from '../../shared/order-status-chip/order-status-chip.component';

const POLL_MS = 1000;
const GIVE_UP_MS = 30_000;
const STEPS: OrderStatus[] = ['Placed', 'Paid', 'Shipped', 'Delivered'];

const REASONS: Record<string, string> = {
  OutOfStock: 'an item sold out',
  PaymentFailed: 'payment was declined',
  PaymentExpired: "payment wasn't completed in time",
  Admin: 'cancelled by the store',
};

interface Step {
  label: string;
  at: string | null;
  state: 'reached' | 'future' | 'cancelled';
}

/** `/orders/:id`: one order with its status timeline; also the return page after paying. */
@Component({
  selector: 'app-order-detail-page',
  imports: [
    DatePipe,
    MatButtonModule,
    MatIconModule,
    MoneyPipe,
    OrderStatusChipComponent,
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
  protected readonly showCancelledBanner = computed(
    () => this.checkout() === 'cancelled' && this.order()?.status === 'Placed',
  );

  protected readonly steps = computed<Step[]>(() => {
    const order = this.order();
    if (!order) return [];
    const at = (s: OrderStatus) => order.history.find((h) => h.status === s)?.atUtc ?? null;
    const rank = STEPS.indexOf(order.status);
    const cancelled = order.status === 'Cancelled';
    const reached = STEPS.map<Step>((label, i) => {
      const time = at(label);
      const done = time !== null || (!cancelled && i <= rank);
      return { label, at: time, state: done ? 'reached' : 'future' };
    });
    if (!cancelled) return reached;
    const before = reached.filter((s) => s.state === 'reached');
    const reason = order.cancellationReason ? REASONS[order.cancellationReason] : null;
    return [
      ...before,
      {
        label: reason ? `Cancelled: ${reason}` : 'Cancelled',
        at: at('Cancelled'),
        state: 'cancelled',
      },
    ];
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
    if (this.checkout() === 'success') this.pollUntilSettled();
    else if (this.checkout() === 'cancelled') this.loadPaymentUrl();
  }

  /** The webhook may land a moment after the redirect, so re-read the order until it moves on. */
  private pollUntilSettled(): void {
    this.confirming.set(true);
    const id = this.id();
    this.subs.push(
      timer(POLL_MS, POLL_MS)
        .pipe(
          exhaustMap(() => this.api.order(id)),
          tap((order) => this.order.set(order)),
          takeWhile((order) => order.status === 'Placed'),
          takeUntil(timer(GIVE_UP_MS)),
        )
        .subscribe({
          error: () => this.confirming.set(false),
          complete: () => this.confirming.set(false),
        }),
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
