import { HttpErrorResponse } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { Subject, of, throwError } from 'rxjs';
import { AdminApi, AdminOrderDto } from '../admin-api.service';
import { AdminOrderDetailPage } from './admin-order-detail.page';

const base: AdminOrderDto = {
  id: 'o1',
  orderNumber: 'VL-00042',
  placedAtUtc: '2026-11-20T10:00:00Z',
  status: 'Paid',
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
  history: [
    { status: 'Placed', atUtc: '2026-11-20T10:00:00Z', note: null },
    { status: 'Paid', atUtc: '2026-11-20T10:01:00Z', note: null },
  ],
  customerEmail: 'ada@x.io',
  customerId: 'c1',
  refundRequired: false,
};

function setup(order: AdminOrderDto, extra: Record<string, unknown> = {}) {
  const api = {
    order: vi.fn().mockReturnValue(of(order)),
    ship: vi.fn(),
    deliver: vi.fn(),
    ...extra,
  };
  TestBed.configureTestingModule({
    providers: [provideRouter([]), { provide: AdminApi, useValue: api }],
  });
  const fixture = TestBed.createComponent(AdminOrderDetailPage);
  fixture.componentRef.setInput('id', 'o1');
  fixture.detectChanges();
  const el = fixture.nativeElement as HTMLElement;
  const button = (text: string) =>
    [...el.querySelectorAll<HTMLButtonElement>('button')].find((b) => b.textContent?.includes(text));
  return { api, fixture, el, button };
}

describe('AdminOrderDetailPage', () => {
  it('shows the order, customer email, address and timeline', () => {
    const { el } = setup(base);
    expect(el.textContent).toContain('VL-00042');
    expect(el.textContent).toContain('ada@x.io');
    expect(el.textContent).toContain('Volt Phone');
    expect(el.textContent).toContain('Ada Lovelace');
    expect(el.querySelectorAll('.timeline li').length).toBe(4);
  });

  it('offers only "Mark as shipped" for a Paid order', () => {
    const { button } = setup(base);
    expect(button('Mark as shipped')).toBeDefined();
    expect(button('Mark as delivered')).toBeUndefined();
  });

  it('offers only "Mark as delivered" for a Shipped order', () => {
    const { button } = setup({ ...base, status: 'Shipped' });
    expect(button('Mark as shipped')).toBeUndefined();
    expect(button('Mark as delivered')).toBeDefined();
  });

  it.each(['Placed', 'Delivered', 'Cancelled'] as const)('offers no action for %s', (status) => {
    const { button } = setup({ ...base, status });
    expect(button('Mark as shipped')).toBeUndefined();
    expect(button('Mark as delivered')).toBeUndefined();
  });

  it('ships, disables the button while pending, then shows the new status', () => {
    const result = new Subject<AdminOrderDto>();
    const { api, fixture, el, button } = setup(base, { ship: vi.fn().mockReturnValue(result) });
    button('Mark as shipped')!.click();
    fixture.detectChanges();
    expect(api.ship).toHaveBeenCalledWith('o1');
    expect(button('Mark as shipped')!.disabled).toBe(true);

    result.next({
      ...base,
      status: 'Shipped',
      history: [...base.history, { status: 'Shipped', atUtc: '2026-11-21T09:00:00Z', note: null }],
    });
    result.complete();
    fixture.detectChanges();
    expect(el.querySelector('app-order-status-chip')!.textContent).toContain('Shipped');
    expect(button('Mark as shipped')).toBeUndefined();
    expect(button('Mark as delivered')).toBeDefined();
  });

  it('shows the problem title on 409 and reloads the order', () => {
    const conflict = new HttpErrorResponse({
      status: 409,
      error: { title: 'ordering.invalid_transition' },
    });
    const { api, fixture, el, button } = setup(base, {
      ship: vi.fn().mockReturnValue(throwError(() => conflict)),
    });
    button('Mark as shipped')!.click();
    fixture.detectChanges();
    expect(el.querySelector('[role="alert"]')?.textContent).toContain('ordering.invalid_transition');
    expect(api.order).toHaveBeenCalledTimes(2);
  });

  it('shows the refund banner when a refund is required', () => {
    const { el } = setup({ ...base, status: 'Cancelled', refundRequired: true });
    expect(el.textContent).toContain('Refund needed — handle in the Stripe dashboard.');
  });

  it('shows not found on a 404', () => {
    const { el } = setup(base, {
      order: vi.fn().mockReturnValue(throwError(() => new HttpErrorResponse({ status: 404 }))),
    });
    expect(el.textContent).toContain('Order not found');
  });
});
