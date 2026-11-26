import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { of } from 'rxjs';
import { CheckoutStatus, OrderingApi } from '../../api/ordering-api.service';
import { RedirectService } from '../../core/redirect.service';
import { CheckoutProgressPage } from './checkout-progress.page';

function setup(status: CheckoutStatus) {
  const api = { checkoutStatus: vi.fn().mockReturnValue(of(status)) };
  const redirect = { to: vi.fn() };
  TestBed.configureTestingModule({
    providers: [
      provideRouter([]),
      { provide: OrderingApi, useValue: api },
      { provide: RedirectService, useValue: redirect },
    ],
  });
  const nav = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
  const fixture = TestBed.createComponent(CheckoutProgressPage);
  fixture.componentRef.setInput('id', 'o1');
  fixture.detectChanges();
  return { api, redirect, nav, fixture, el: fixture.nativeElement as HTMLElement };
}

describe('CheckoutProgressPage', () => {
  beforeEach(() => vi.useFakeTimers());
  afterEach(() => vi.useRealTimers());

  it('redirects to the payment page when ReadyToPay', () => {
    const { redirect } = setup({ state: 'ReadyToPay', paymentUrl: 'https://pay.example/x' });
    vi.advanceTimersByTime(10);
    expect(redirect.to).toHaveBeenCalledWith('https://pay.example/x');
  });

  it('shows the out-of-stock copy', () => {
    const { fixture, el } = setup({ state: 'OutOfStock' });
    vi.advanceTimersByTime(10);
    fixture.detectChanges();
    expect(el.textContent).toContain('Sorry, an item sold out while we were reserving it.');
    expect(el.textContent).toContain('Back to cart');
  });

  it('shows the failed copy', () => {
    const { fixture, el } = setup({ state: 'Failed' });
    vi.advanceTimersByTime(10);
    fixture.detectChanges();
    expect(el.textContent).toContain("We couldn't start your payment.");
    expect(el.textContent).toContain('View order');
  });

  it('goes to the order when Paid', () => {
    const { nav } = setup({ state: 'Paid' });
    vi.advanceTimersByTime(10);
    expect(nav).toHaveBeenCalledWith(['/orders', 'o1'], { replaceUrl: true });
  });

  it('polls every 500 ms and stops on destroy', () => {
    const { api, fixture } = setup({ state: 'Pending' });
    vi.advanceTimersByTime(1100);
    const calls = api.checkoutStatus.mock.calls.length;
    expect(calls).toBeGreaterThanOrEqual(3);
    fixture.destroy();
    vi.advanceTimersByTime(5000);
    expect(api.checkoutStatus.mock.calls.length).toBe(calls);
  });

  it('shows the timeout copy after 30 s and Check again restarts polling', () => {
    const { api, fixture, el } = setup({ state: 'Pending' });
    vi.advanceTimersByTime(30_100);
    fixture.detectChanges();
    expect(el.textContent).toContain('This is taking longer than usual.');
    const calls = api.checkoutStatus.mock.calls.length;
    vi.advanceTimersByTime(2000);
    expect(api.checkoutStatus.mock.calls.length).toBe(calls);
    [...el.querySelectorAll('button')].find((b) => b.textContent?.includes('Check again'))!.click();
    vi.advanceTimersByTime(600);
    expect(api.checkoutStatus.mock.calls.length).toBeGreaterThan(calls);
  });
});
