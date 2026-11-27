import { HttpErrorResponse } from '@angular/common/http';
import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { of, throwError } from 'rxjs';
import { OrderingApi } from '../../api/ordering-api.service';
import { AuthService } from '../../core/auth/auth.service';
import { Basket, BasketService } from '../../core/basket/basket.service';
import { CheckoutPage } from './checkout.page';

const basket: Basket = {
  lines: [
    {
      variantId: 'v1',
      productSlug: 'aurora-x',
      productName: 'Aurora X',
      brand: 'Voltra',
      sku: 'BLK',
      optionsLabel: 'Black',
      thumbnailUrl: null,
      unitPrice: 100,
      quantity: 2,
      available: 5,
      lineTotal: 200,
      issue: null,
    },
  ],
  itemCount: 2,
  subtotal: 200,
  hasIssues: false,
};

const problem = (type: string) =>
  new HttpErrorResponse({ status: 409, error: { type, title: 't' }, statusText: 'x' });

function setup(isDemo = false) {
  const api = { checkout: vi.fn() };
  const cart = { basket: signal<Basket | null>(basket), load: vi.fn().mockResolvedValue(undefined) };
  TestBed.configureTestingModule({
    providers: [
      provideRouter([]),
      { provide: OrderingApi, useValue: api },
      { provide: BasketService, useValue: cart },
      { provide: AuthService, useValue: { user: signal({ isDemo }) } },
    ],
  });
  const router = TestBed.inject(Router);
  const nav = vi.spyOn(router, 'navigate').mockResolvedValue(true);
  const fixture = TestBed.createComponent(CheckoutPage);
  const el = fixture.nativeElement as HTMLElement;
  const fill = () => {
    const values: Record<string, string> = {
      fullName: 'Ada Lovelace',
      line1: '1 Main St',
      city: 'London',
      postalCode: 'N1 1AA',
    };
    for (const [name, v] of Object.entries(values)) {
      const input = el.querySelector<HTMLInputElement>(`[formcontrolname="${name}"]`)!;
      input.value = v;
      input.dispatchEvent(new Event('input'));
    }
  };
  const submit = async () => {
    el.querySelector<HTMLFormElement>('form')!.dispatchEvent(new Event('submit'));
    await fixture.whenStable();
  };
  return { api, cart, nav, fixture, el, fill, submit };
}

describe('CheckoutPage', () => {
  it('redirects to the progress page on 202', async () => {
    const { api, nav, fixture, fill, submit } = setup();
    api.checkout.mockReturnValue(of({ orderId: 'o1', orderNumber: 'VL-1' }));
    await fixture.whenStable();
    fill();
    await submit();
    expect(api.checkout).toHaveBeenCalledTimes(1);
    expect(api.checkout.mock.calls[0][1]).toMatchObject({
      expectedTotal: 200,
      shippingAddress: { fullName: 'Ada Lovelace', country: 'US' },
    });
    expect(nav).toHaveBeenCalledWith(['/orders', 'o1', 'progress'], { replaceUrl: true });
  });

  it('reuses the same idempotency key on retry and shows the changed-cart message on price_changed', async () => {
    const { api, cart, fixture, el, fill, submit } = setup();
    api.checkout.mockReturnValue(throwError(() => problem('ordering.price_changed')));
    await fixture.whenStable();
    fill();
    await submit();
    await submit();
    expect(api.checkout).toHaveBeenCalledTimes(2);
    expect(api.checkout.mock.calls[0][0]).toBe(api.checkout.mock.calls[1][0]);
    expect(api.checkout.mock.calls[0][0]).toMatch(/^[0-9a-f-]{36}$/);
    expect(cart.load).toHaveBeenCalled();
    fixture.detectChanges();
    expect(el.textContent).toContain('Your cart changed. Please review it before paying.');
    expect(el.querySelector('a[href="/cart"]')).not.toBeNull();
  });

  it('goes to the cart when the basket is empty', async () => {
    const { api, nav, fixture, fill, submit } = setup();
    api.checkout.mockReturnValue(throwError(() => problem('ordering.empty_basket')));
    await fixture.whenStable();
    fill();
    await submit();
    expect(nav).toHaveBeenCalledWith(['/cart']);
  });

  it('does not submit an invalid form', async () => {
    const { api, fixture, submit } = setup();
    await fixture.whenStable();
    await submit();
    expect(api.checkout).not.toHaveBeenCalled();
  });

  it('pre-fills the demo address for demo customers', async () => {
    const { fixture, el } = setup(true);
    await fixture.whenStable();
    expect(el.querySelector<HTMLInputElement>('[formcontrolname="line1"]')!.value).toBe(
      '221 Market Street',
    );
    expect(el.querySelector<HTMLInputElement>('[formcontrolname="postalCode"]')!.value).toBe(
      '94105',
    );
  });
});
