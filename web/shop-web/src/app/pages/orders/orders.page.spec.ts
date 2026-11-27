import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { NEVER, of } from 'rxjs';
import { OrderingApi } from '../../api/ordering-api.service';
import { OrdersPage } from './orders.page';

const order = (n: number) => ({
  id: `o${n}`,
  orderNumber: `VL-0000${n}`,
  placedAtUtc: '2026-11-20T10:00:00Z',
  status: 'Paid' as const,
  total: 120.5,
  itemCount: 2,
  firstThumbnailUrl: null,
});

function setup(result: unknown) {
  const api = { orders: vi.fn().mockReturnValue(result) };
  TestBed.configureTestingModule({
    providers: [provideRouter([]), { provide: OrderingApi, useValue: api }],
  });
  const fixture = TestBed.createComponent(OrdersPage);
  fixture.detectChanges();
  return { api, fixture, el: fixture.nativeElement as HTMLElement };
}

describe('OrdersPage', () => {
  it('shows skeletons while loading', () => {
    const { el } = setup(NEVER);
    expect(el.querySelector('[aria-busy="true"]')).not.toBeNull();
  });

  it('renders a card per order with number, status, items and total', () => {
    const { api, el } = setup(
      of({ items: [order(1), order(2)], page: 1, pageSize: 10, totalCount: 2, totalPages: 1 }),
    );
    expect(api.orders).toHaveBeenCalledWith(1, 10);
    expect(el.querySelectorAll('a.order-card').length).toBe(2);
    expect(el.textContent).toContain('VL-00001');
    expect(el.textContent).toContain('Paid');
    expect(el.textContent).toContain('2 items');
    expect(el.textContent).toContain('$120.50');
    expect(el.querySelector('a.order-card')!.getAttribute('href')).toBe('/orders/o1');
  });

  it('shows the empty state', () => {
    const { el } = setup(of({ items: [], page: 1, pageSize: 10, totalCount: 0, totalPages: 0 }));
    expect(el.textContent).toContain('No orders yet');
    expect(el.textContent).toContain('Start shopping');
  });
});
