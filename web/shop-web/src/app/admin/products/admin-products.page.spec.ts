import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { NEVER, of } from 'rxjs';
import { AdminApi } from '../admin-api.service';
import { AdminProductsPage } from './admin-products.page';

const product = (n: number, over: Record<string, unknown> = {}) => ({
  id: `p${n}`,
  slug: `phone-${n}`,
  name: `Phone ${n}`,
  brand: 'Volt',
  categoryName: 'Phones',
  status: 'Active' as const,
  isFeatured: false,
  variantCount: 3,
  totalStock: 40,
  totalReserved: 0,
  minPrice: 499,
  thumbnailUrl: '/img/t.jpg',
  ...over,
});
const page = (items: unknown[], totalCount = items.length) => ({
  items,
  page: 1,
  pageSize: 20,
  totalCount,
  totalPages: Math.ceil(totalCount / 20),
});

function setup(result: unknown, inputs: Record<string, unknown> = {}) {
  const api = { products: vi.fn().mockReturnValue(result) };
  TestBed.configureTestingModule({
    providers: [provideRouter([]), { provide: AdminApi, useValue: api }],
  });
  const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
  const fixture = TestBed.createComponent(AdminProductsPage);
  for (const [k, v] of Object.entries(inputs)) fixture.componentRef.setInput(k, v);
  fixture.detectChanges();
  return { api, navigate, fixture, el: fixture.nativeElement as HTMLElement };
}

describe('AdminProductsPage', () => {
  beforeEach(() => vi.useFakeTimers());
  afterEach(() => vi.useRealTimers());

  it('shows skeletons while loading', () => {
    const { el } = setup(NEVER);
    expect(el.querySelector('[aria-busy="true"]')).not.toBeNull();
  });

  it('renders a row per product with stock, reserved and from-price', () => {
    const { api, el } = setup(
      of(page([product(1), product(2, { totalReserved: 3, status: 'Draft' })])),
    );
    expect(api.products).toHaveBeenCalledWith({ status: '', q: '', page: 1, pageSize: 20 });
    const rows = el.querySelectorAll('tr.row');
    expect(rows.length).toBe(2);
    expect(rows[0].textContent).toContain('Phone 1');
    expect(rows[0].textContent).toContain('Volt');
    expect(rows[0].textContent).toContain('Phones');
    expect(rows[0].textContent).toContain('Active');
    expect(rows[0].textContent).toContain('from $499.00');
    expect(rows[0].textContent).not.toContain('reserved');
    expect(rows[1].textContent).toContain('40 · 3 reserved');
    expect(rows[1].textContent).toContain('Draft');
    expect(rows[0].querySelector('a')!.getAttribute('href')).toBe('/admin/products/p1');
  });

  it('links to the create page', () => {
    const { el } = setup(of(page([product(1)])));
    const link = [...el.querySelectorAll('a')].find((a) => a.textContent?.includes('New product'))!;
    expect(link.getAttribute('href')).toBe('/admin/products/new');
  });

  it('shows the empty state', () => {
    const { el } = setup(of(page([])));
    expect(el.textContent).toContain('No products match.');
  });

  it('sends status, q and page from the URL inputs', () => {
    const { api } = setup(of(page([product(1)], 45)), { status: 'Draft', q: 'volt', page: '2' });
    expect(api.products).toHaveBeenCalledWith({ status: 'Draft', q: 'volt', page: 2, pageSize: 20 });
  });

  it('writes the chosen status to the URL and resets to page 1', () => {
    const { navigate, el } = setup(of(page([product(1)])), { page: '3' });
    const chips = [...el.querySelectorAll<HTMLElement>('mat-chip-option')];
    expect(chips.map((c) => c.textContent?.trim())).toEqual(['All', 'Draft', 'Active', 'Archived']);
    chips[1].querySelector<HTMLElement>('button')!.click();
    expect(navigate).toHaveBeenCalledWith([], {
      queryParams: { status: 'Draft', page: null },
      queryParamsHandling: 'merge',
    });
  });

  it('debounces the search by 300 ms before writing q to the URL', () => {
    const { navigate, el } = setup(of(page([product(1)])));
    const input = el.querySelector<HTMLInputElement>('input[type="search"]')!;
    input.value = 'volt';
    input.dispatchEvent(new Event('input'));
    vi.advanceTimersByTime(299);
    expect(navigate).not.toHaveBeenCalled();
    vi.advanceTimersByTime(1);
    expect(navigate).toHaveBeenCalledWith([], {
      queryParams: { q: 'volt', page: null },
      queryParamsHandling: 'merge',
    });
  });

  it('pages through results', () => {
    vi.spyOn(window, 'scrollTo').mockImplementation(() => undefined);
    const { navigate, fixture } = setup(of(page([product(1)], 45)));
    const paginator = fixture.debugElement.query((d) => d.name === 'mat-paginator');
    paginator.componentInstance.page.emit({ pageIndex: 1, pageSize: 20, length: 45 });
    expect(navigate).toHaveBeenCalledWith([], {
      queryParams: { page: 2 },
      queryParamsHandling: 'merge',
    });
  });
});
