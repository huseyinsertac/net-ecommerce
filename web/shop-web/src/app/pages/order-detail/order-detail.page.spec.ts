import { HttpErrorResponse } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { of, throwError } from 'rxjs';
import { OrderDto, OrderingApi } from '../../api/ordering-api.service';
import { RedirectService } from '../../core/redirect.service';
import { OrderDetailPage } from './order-detail.page';

const base: OrderDto = {
  id: 'o1',
  orderNumber: 'VL-00042',
  placedAtUtc: '2026-11-20T10:00:00Z',
  status: 'Placed',
  cancellationReason: null,
  lines: [
    {
      variantId: 'v1',
      productName: 'Volt Phone',
      sku: 'VP-1',
      optionsLabel: 'Black / 128 GB',
      thumbnailUrl: null,
      unitPrice: 100,
      quantity: 2,
      lineTotal: 200,
    },
  ],
  shippingAddress: {
    fullName: 'Ada Lovelace',
    line1: '1 Main St',
    line2: null,
    city: 'London',
    postalCode: 'N1',
    country: 'GB',
  },
  subtotal: 200,
  discount: 0,
  total: 200,
  history: [{ status: 'Placed', atUtc: '2026-11-20T10:00:00Z', note: null }],
};

function setup(
  orders: OrderDto[] | 'notfound',
  checkout?: string,
  state: { state: string; paymentUrl?: string } = { state: 'Pending' },
) {
  let i = 0;
  const api = {
    order: vi.fn().mockImplementation(() =>
      orders === 'notfound'
        ? throwError(() => new HttpErrorResponse({ status: 404 }))
        : of(orders[Math.min(i++, orders.length - 1)]),
    ),
    checkoutStatus: vi.fn().mockReturnValue(of(state)),
  };
  const redirect = { to: vi.fn() };
  TestBed.configureTestingModule({
    providers: [
      provideRouter([]),
      { provide: OrderingApi, useValue: api },
      { provide: RedirectService, useValue: redirect },
    ],
  });
  const fixture = TestBed.createComponent(OrderDetailPage);
  fixture.componentRef.setInput('id', 'o1');
  if (checkout) fixture.componentRef.setInput('checkout', checkout);
  fixture.detectChanges();
  return { api, redirect, fixture, el: fixture.nativeElement as HTMLElement };
}

describe('OrderDetailPage', () => {
  beforeEach(() => vi.useFakeTimers());
  afterEach(() => vi.useRealTimers());

  it('shows header, lines, address and totals', () => {
    const { el } = setup([base]);
    expect(el.textContent).toContain('VL-00042');
    expect(el.textContent).toContain('Volt Phone');
    expect(el.textContent).toContain('Black / 128 GB');
    expect(el.textContent).toContain('Ada Lovelace');
    expect(el.textContent).toContain('$200.00');
  });

  it('marks reached timeline steps and greys future ones', () => {
    const paid: OrderDto = {
      ...base,
      status: 'Paid',
      history: [...base.history, { status: 'Paid', atUtc: '2026-11-20T10:05:00Z', note: null }],
    };
    const { el } = setup([paid]);
    const steps = [...el.querySelectorAll('.timeline li')];
    expect(steps.map((s) => s.classList.contains('reached'))).toEqual([true, true, false, false]);
    expect(steps[2].classList.contains('future')).toBe(true);
    expect(steps[0].textContent).toContain('Placed');
    expect(steps[1].querySelector('time')).not.toBeNull();
    expect(steps[3].querySelector('time')).toBeNull();
  });

  it('renders the cancellation reason in human words', () => {
    const { el } = setup([
      {
        ...base,
        status: 'Cancelled',
        cancellationReason: 'PaymentExpired',
        history: [
          ...base.history,
          { status: 'Cancelled', atUtc: '2026-11-20T11:00:00Z', note: null },
        ],
      },
    ]);
    expect(el.textContent).toContain("Cancelled: payment wasn't completed in time");
    expect(el.querySelector('.timeline li.cancelled')).not.toBeNull();
    expect(el.querySelectorAll('.timeline li').length).toBe(2);
  });

  it('polls after checkout=success and shows the thank-you banner once Paid', () => {
    const paid: OrderDto = { ...base, status: 'Paid' };
    const { api, fixture, el } = setup([base, base, paid], 'success');
    expect(el.textContent).toContain('Confirming your payment');
    vi.advanceTimersByTime(2100);
    fixture.detectChanges();
    expect(el.textContent).toContain('Thank you! Order VL-00042 is paid.');
    const calls = api.order.mock.calls.length;
    vi.advanceTimersByTime(5000);
    expect(api.order.mock.calls.length).toBe(calls);
  });

  it('stops polling after 30 s', () => {
    const { api, fixture, el } = setup([base], 'success');
    vi.advanceTimersByTime(31_000);
    fixture.detectChanges();
    const calls = api.order.mock.calls.length;
    vi.advanceTimersByTime(5000);
    expect(api.order.mock.calls.length).toBe(calls);
    expect(el.textContent).not.toContain('Confirming your payment');
  });

  it('shows Pay now only when checkout-status is ReadyToPay', () => {
    const { el, redirect } = setup([base], 'cancelled', {
      state: 'ReadyToPay',
      paymentUrl: 'https://pay/x',
    });
    expect(el.textContent).toContain('Payment not completed.');
    [...el.querySelectorAll('button')].find((b) => b.textContent?.includes('Pay now'))!.click();
    expect(redirect.to).toHaveBeenCalledWith('https://pay/x');
  });

  it('hides Pay now when the payment is not ready', () => {
    const { el } = setup([base], 'cancelled', { state: 'Failed' });
    expect(el.textContent).toContain('Payment not completed.');
    expect(el.textContent).not.toContain('Pay now');
  });

  it('offers Pay now on a Placed order opened without a query param when the payment is ready', () => {
    const { api, el, redirect } = setup([base], undefined, {
      state: 'ReadyToPay',
      paymentUrl: 'https://pay/y',
    });
    expect(api.checkoutStatus).toHaveBeenCalledTimes(1);
    expect(el.textContent).toContain('This order is waiting for payment.');
    expect(el.textContent).not.toContain('Payment not completed.');
    [...el.querySelectorAll('button')].find((b) => b.textContent?.includes('Pay now'))!.click();
    expect(redirect.to).toHaveBeenCalledWith('https://pay/y');
  });

  it('shows no Pay now on a Placed order while the payment is still Pending', () => {
    const { api, el } = setup([base]);
    expect(api.checkoutStatus).toHaveBeenCalledTimes(1);
    expect(el.textContent).not.toContain('Pay now');
    expect(el.textContent).not.toContain('This order is waiting for payment.');
  });

  it('does not ask for the payment after checkout=success or on a non-Placed order', () => {
    expect(setup([base], 'success').api.checkoutStatus).not.toHaveBeenCalled();
    TestBed.resetTestingModule();
    expect(setup([{ ...base, status: 'Paid' }]).api.checkoutStatus).not.toHaveBeenCalled();
  });

  it('polls after checkout=cancelled and shows the Cancelled timeline once it lands', () => {
    const cancelled: OrderDto = {
      ...base,
      status: 'Cancelled',
      cancellationReason: 'PaymentFailed',
      history: [...base.history, { status: 'Cancelled', atUtc: '2026-11-20T10:01:00Z', note: null }],
    };
    const { api, fixture, el } = setup([base, base, cancelled], 'cancelled', { state: 'Failed' });
    expect(el.textContent).toContain('Payment not completed.');
    vi.advanceTimersByTime(2100);
    fixture.detectChanges();
    expect(el.textContent).toContain('Cancelled: payment was declined');
    expect(el.textContent).not.toContain('Payment not completed.');
    const calls = api.order.mock.calls.length;
    vi.advanceTimersByTime(5000);
    expect(api.order.mock.calls.length).toBe(calls);
  });

  it('stops polling after checkout=cancelled after 10 s', () => {
    const { api } = setup([base], 'cancelled');
    vi.advanceTimersByTime(11_000);
    const calls = api.order.mock.calls.length;
    expect(calls).toBeGreaterThan(5);
    vi.advanceTimersByTime(5000);
    expect(api.order.mock.calls.length).toBe(calls);
  });

  it('renders the not-found state on 404', () => {
    const { el } = setup('notfound');
    expect(el.textContent).toContain('Order not found');
  });
});
