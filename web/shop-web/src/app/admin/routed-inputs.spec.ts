import { provideHttpClient } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { provideRouter, withComponentInputBinding } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { of } from 'rxjs';
import { AdminApi } from './admin-api.service';
import { AdminOrdersPage } from './orders/admin-orders.page';
import { AdminProductsPage } from './products/admin-products.page';

const empty = { items: [], page: 1, pageSize: 20, totalCount: 0, totalPages: 0 };

describe('admin list pages routed with no query params', () => {
  async function open(url: string, component: typeof AdminOrdersPage | typeof AdminProductsPage) {
    const api = { orders: vi.fn().mockReturnValue(of(empty)), products: vi.fn().mockReturnValue(of(empty)) };
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideRouter([{ path: url.slice(1), component }], withComponentInputBinding()),
        { provide: AdminApi, useValue: api },
      ],
    });
    const harness = await RouterTestingHarness.create();
    await harness.navigateByUrl(url);
    harness.detectChanges();
    return { api, el: harness.routeNativeElement as HTMLElement };
  }

  for (const [url, component, call] of [
    ['/admin/orders', AdminOrdersPage, 'orders'],
    ['/admin/products', AdminProductsPage, 'products'],
  ] as const) {
    it(`${url}: empty search box, "All" chip selected, default query`, async () => {
      const { api, el } = await open(url, component);
      const input = el.querySelector('input[type="search"]') as HTMLInputElement;
      expect(input.value).toBe('');
      const all = el.querySelector('mat-chip-option') as HTMLElement;
      expect(all.textContent).toContain('All');
      expect(all.classList.contains('mdc-evolution-chip--selected')).toBe(true);
      expect(api[call]).toHaveBeenCalledWith({ status: '', q: '', page: 1, pageSize: 20 });
    });
  }
});
