import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { NEVER, of } from 'rxjs';
import { AdminApi } from '../admin-api.service';
import { AdminOrdersPage } from './admin-orders.page';

const order = (n: number) => ({
  id: `o${n}`,
  orderNumber: `VL-0000${n}`,
  placedAtUtc: '2026-11-20T10:00:00Z',
  status: 'Paid' as const,
  customerEmail: `buyer${n}@x.io`,
  itemCount: 2,
  total: 120.5,
});
const page = (items: unknown[], totalCount = items.length) => ({
  items,
  page: 1,
  pageSize: 20,
  totalCount,
  totalPages: Math.ceil(totalCount / 20),
});

function setup(result: unknown, inputs: Record<string, unknown> = {}) {
  const api = { orders: vi.fn().mockReturnValue(result) };
  TestBed.configureTestingModule({
    providers: [provideRouter([]), { provide: AdminApi, useValue: api }],
  });
  const router = TestBed.inject(Router);
  const navigate = vi.spyOn(router, 'navigate').mockResolvedValue(true);
  const fixture = TestBed.createComponent(AdminOrdersPage);
  for (const [k, v] of Object.entries(inputs)) fixture.componentRef.setInput(k, v);
  fixture.detectChanges();
  return { api, navigate, fixture, el: fixture.nativeElement as HTMLElement };
}

describe('AdminOrdersPage', () => {
  beforeEach(() => vi.useFakeTimers());
  afterEach(() => vi.useRealTimers());

  it('shows skeletons while loading', () => {
    const { el } = setup(NEVER);
    expect(el.querySelector('[aria-busy="true"]')).not.toBeNull();
  });

  it('renders a row per order linking to the detail page', () => {
    const { api, el } = setup(of(page([order(1), order(2)])));
    expect(api.orders).toHaveBeenCalledWith({ status: '', q: '', page: 1, pageSize: 20 });
    const rows = el.querySelectorAll('tr.row');
    expect(rows.length).toBe(2);
    expect(rows[0].textContent).toContain('VL-00001');
    expect(rows[0].textContent).toContain('buyer1@x.io');
    expect(rows[0].textContent).toContain('Paid');
    expect(rows[0].textContent).toContain('$120.50');
    expect(rows[0].querySelector('a')!.getAttribute('href')).toBe('/admin/orders/o1');
  });

  it('shows the empty state', () => {
    const { el } = setup(of(page([])));
    expect(el.textContent).toContain('No orders match.');
  });

  it('sends status, q and page from the URL inputs', () => {
    const { api } = setup(of(page([order(1)], 45)), { status: 'Shipped', q: 'ada', page: '2' });
    expect(api.orders).toHaveBeenCalledWith({ status: 'Shipped', q: 'ada', page: 2, pageSize: 20 });
  });

  it('writes the chosen status to the URL and resets to page 1', () => {
    const { navigate, el } = setup(of(page([order(1)])), { page: '3' });
    const chips = [...el.querySelectorAll<HTMLElement>('mat-chip-option')];
    expect(chips.map((c) => c.textContent?.trim())).toEqual([
      'All',
      'Placed',
      'Paid',
      'Shipped',
      'Delivered',
      'Cancelled',
    ]);
    chips[3].querySelector<HTMLElement>('button')!.click();
    expect(navigate).toHaveBeenCalledWith([], {
      queryParams: { status: 'Shipped', page: null },
      queryParamsHandling: 'merge',
    });
  });

  it('debounces the search by 300 ms before writing q to the URL', () => {
    const { navigate, el } = setup(of(page([order(1)])));
    const input = el.querySelector<HTMLInputElement>('input[type="search"]')!;
    input.value = 'ada';
    input.dispatchEvent(new Event('input'));
    vi.advanceTimersByTime(299);
    expect(navigate).not.toHaveBeenCalled();
    vi.advanceTimersByTime(1);
    expect(navigate).toHaveBeenCalledWith([], {
      queryParams: { q: 'ada', page: null },
      queryParamsHandling: 'merge',
    });
  });

  it('pages through results', () => {
    vi.spyOn(window, 'scrollTo').mockImplementation(() => undefined);
    const { navigate, fixture } = setup(of(page([order(1)], 45)));
    const paginator = fixture.debugElement.query((d) => d.name === 'mat-paginator');
    paginator.componentInstance.page.emit({ pageIndex: 1, pageSize: 20, length: 45 });
    expect(navigate).toHaveBeenCalledWith([], {
      queryParams: { page: 2 },
      queryParamsHandling: 'merge',
    });
  });
});
