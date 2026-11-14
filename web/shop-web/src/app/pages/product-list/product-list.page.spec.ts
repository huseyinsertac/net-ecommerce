import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { ProductListPage } from './product-list.page';

async function setup(url: string) {
  TestBed.configureTestingModule({
    providers: [
      provideRouter([
        { path: 'c/:slug', component: ProductListPage },
        { path: 'search', component: ProductListPage },
      ]),
      provideHttpClient(),
      provideHttpClientTesting(),
    ],
  });
  const http = TestBed.inject(HttpTestingController);
  const harness = await RouterTestingHarness.create();
  await harness.navigateByUrl(url);
  return { http, harness };
}

describe('ProductListPage', () => {
  it('shows the empty state when totalCount is 0', async () => {
    const { http, harness } = await setup('/search?q=zzz');
    http
      .expectOne((r) => r.url === '/api/catalog/products')
      .flush({ items: [], page: 1, pageSize: 24, totalCount: 0, totalPages: 0 });
    await harness.fixture.whenStable();
    const el = harness.routeNativeElement as HTMLElement;
    expect(el.textContent).toContain('No products match these filters');
    expect(el.textContent).toContain('Clear filters');
  });

  it('renders NotFound for an unknown category', async () => {
    const { http, harness } = await setup('/c/nope');
    const problem = { type: 'catalog.category_not_found', title: 'nope', status: 404 };
    http
      .match((r) => r.url.startsWith('/api/catalog/'))
      .forEach((r) => r.flush(problem, { status: 404, statusText: 'Not Found' }));
    await harness.fixture.whenStable();
    expect((harness.routeNativeElement as HTMLElement).textContent).toContain('Page not found');
  });
});
