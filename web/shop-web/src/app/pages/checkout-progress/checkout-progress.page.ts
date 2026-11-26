import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  OnInit,
  inject,
  input,
  signal,
} from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { Router, RouterLink } from '@angular/router';
import { Subscription, exhaustMap, takeUntil, takeWhile, tap, timer } from 'rxjs';
import { CheckoutStatus, OrderingApi } from '../../api/ordering-api.service';
import { RedirectService } from '../../core/redirect.service';

const POLL_MS = 500;
const GIVE_UP_MS = 30_000;

type View = 'reserving' | 'outOfStock' | 'failed' | 'slow';

/** `/orders/:id/progress`: waits for the stock reservation, then sends the shopper to pay. */
@Component({
  selector: 'app-checkout-progress-page',
  imports: [MatButtonModule, MatProgressSpinnerModule, RouterLink],
  templateUrl: './checkout-progress.page.html',
  styleUrl: './checkout-progress.page.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class CheckoutProgressPage implements OnInit {
  private readonly api = inject(OrderingApi);
  private readonly router = inject(Router);
  private readonly redirect = inject(RedirectService);

  /** Bound from the route (`withComponentInputBinding`). */
  readonly id = input.required<string>();

  protected readonly view = signal<View>('reserving');
  private poll: Subscription | null = null;

  constructor() {
    inject(DestroyRef).onDestroy(() => this.poll?.unsubscribe());
  }

  ngOnInit(): void {
    this.start();
  }

  protected start(): void {
    this.poll?.unsubscribe();
    this.view.set('reserving');
    const id = this.id();
    let settled = false;
    this.poll = timer(0, POLL_MS)
      .pipe(
        // One request at a time, so a slow response never stacks up behind the next tick.
        exhaustMap(() => this.api.checkoutStatus(id)),
        tap((status) => {
          settled = status.state !== 'Pending';
          this.apply(id, status);
        }),
        takeWhile((status) => status.state === 'Pending'),
        takeUntil(timer(GIVE_UP_MS)),
      )
      .subscribe({
        // A request error ends the poll without a result; ask the shopper to try again.
        error: () => this.view.set('slow'),
        complete: () => {
          if (!settled) this.view.set('slow');
        },
      });
  }

  private apply(id: string, status: CheckoutStatus): void {
    switch (status.state) {
      case 'ReadyToPay':
        if (status.paymentUrl) this.redirect.to(status.paymentUrl);
        else this.view.set('failed');
        break;
      case 'OutOfStock':
        this.view.set('outOfStock');
        break;
      case 'Failed':
        this.view.set('failed');
        break;
      case 'Paid':
        void this.router.navigate(['/orders', id], { replaceUrl: true });
        break;
    }
  }
}
