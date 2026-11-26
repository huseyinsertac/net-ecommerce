import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSelectModule } from '@angular/material/select';
import { Router, RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { OrderingApi } from '../../api/ordering-api.service';
import { AuthService } from '../../core/auth/auth.service';
import { BasketService } from '../../core/basket/basket.service';
import { MoneyPipe } from '../../core/money.pipe';

const DEMO_ADDRESS = {
  fullName: 'Demo Customer',
  line1: '221 Market Street',
  line2: '',
  city: 'San Francisco',
  postalCode: '94105',
  country: 'US',
};

/** `/checkout`: address form and order summary; places the order and hands over to the progress page. */
@Component({
  selector: 'app-checkout-page',
  imports: [
    ReactiveFormsModule,
    RouterLink,
    MatButtonModule,
    MatFormFieldModule,
    MatInputModule,
    MatProgressSpinnerModule,
    MatSelectModule,
    MoneyPipe,
  ],
  templateUrl: './checkout.page.html',
  styleUrl: './checkout.page.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class CheckoutPage {
  private readonly api = inject(OrderingApi);
  private readonly router = inject(Router);
  protected readonly cart = inject(BasketService);

  protected readonly countries = ['US', 'GB', 'DE', 'NL', 'FR', 'TR'];
  /** One key per visit to this page, so a retry after a failure can never place a second order. */
  private readonly idempotencyKey = crypto.randomUUID();

  protected readonly form = inject(FormBuilder).nonNullable.group({
    fullName: ['', Validators.required],
    line1: ['', Validators.required],
    line2: [''],
    city: ['', Validators.required],
    postalCode: ['', Validators.required],
    country: ['US', Validators.required],
  });
  protected readonly busy = signal(false);
  protected readonly cartChanged = signal(false);
  protected readonly error = signal('');

  constructor() {
    if (inject(AuthService).user()?.isDemo) this.form.patchValue(DEMO_ADDRESS);
    void this.cart.load();
  }

  protected async submit(): Promise<void> {
    this.form.markAllAsTouched();
    const basket = this.cart.basket();
    if (this.form.invalid || this.busy() || !basket) return;
    this.busy.set(true);
    this.cartChanged.set(false);
    this.error.set('');
    const { line2, ...address } = this.form.getRawValue();
    try {
      const { orderId } = await firstValueFrom(
        this.api.checkout(this.idempotencyKey, {
          shippingAddress: { ...address, line2: line2.trim() || null },
          expectedTotal: basket.subtotal,
        }),
      );
      await this.router.navigate(['/orders', orderId, 'progress'], { replaceUrl: true });
    } catch (err) {
      await this.fail(err);
    } finally {
      this.busy.set(false);
    }
  }

  private async fail(err: unknown): Promise<void> {
    const http = err instanceof HttpErrorResponse ? err : null;
    const type = (http?.error as { type?: string } | null)?.type;
    if (type === 'ordering.price_changed' || type === 'ordering.basket_has_issues') {
      this.cartChanged.set(true);
      await this.cart.load();
    } else if (type === 'ordering.empty_basket') {
      await this.router.navigate(['/cart']);
    } else if (http && (http.status === 0 || http.status >= 500)) {
      // The error interceptor already toasted it.
    } else {
      this.error.set("We couldn't place your order. Please check your details and try again.");
    }
  }
}
