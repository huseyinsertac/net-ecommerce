import { ChangeDetectionStrategy, Component, OnInit, inject, input, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { Router } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { OrderDto, OrderingApi } from '../../api/ordering-api.service';
import { PaymentsApi } from '../../api/payments-api.service';
import { MoneyPipe } from '../../core/money.pipe';

/** `/pay/simulated/:orderId`: stands in for the card form when no real payment provider is configured. */
@Component({
  selector: 'app-simulated-pay-page',
  imports: [MatButtonModule, MatProgressSpinnerModule, MoneyPipe],
  templateUrl: './simulated-pay.page.html',
  styleUrl: './simulated-pay.page.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class SimulatedPayPage implements OnInit {
  private readonly ordering = inject(OrderingApi);
  private readonly payments = inject(PaymentsApi);
  private readonly router = inject(Router);

  /** Bound from the route (`withComponentInputBinding`). */
  readonly orderId = input.required<string>();

  protected readonly order = signal<OrderDto | null>(null);
  protected readonly loadFailed = signal(false);
  protected readonly busy = signal<'pay' | 'decline' | null>(null);
  protected readonly error = signal('');

  async ngOnInit(): Promise<void> {
    try {
      this.order.set(await firstValueFrom(this.ordering.order(this.orderId())));
    } catch {
      this.loadFailed.set(true);
    }
  }

  protected pay(): Promise<void> {
    return this.finish(true);
  }

  protected decline(): Promise<void> {
    return this.finish(false);
  }

  private async finish(succeed: boolean): Promise<void> {
    if (this.busy()) return;
    this.busy.set(succeed ? 'pay' : 'decline');
    this.error.set('');
    const id = this.orderId();
    try {
      await firstValueFrom(this.payments.simulate(id, succeed), { defaultValue: undefined });
      await this.router.navigate(['/orders', id], {
        queryParams: { checkout: succeed ? 'success' : 'cancelled' },
      });
    } catch {
      this.error.set("We couldn't record that. Please try again.");
    } finally {
      this.busy.set(null);
    }
  }
}
