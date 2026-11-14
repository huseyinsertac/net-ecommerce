import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, TestRequest, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { ProductListPage } from './product-list.page';

async function setup(url: string) {
  vi.spyOn(window, 'scrollTo').mockImplementation(() => undefined);
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

  describe('URL navigation', () => {
    const facets = {
      slug: 'laptops',
      name: 'Laptops',
      brands: [{ value: 'Voltra', count: 3 }],
      minPrice: 1,
      maxPrice: 9,
      specs: [],
    };
    const item = {
      id: '1',
      slug: 'a',
      name: 'A',
      brand: 'Voltra',
      categorySlug: 'laptops',
      imageUrl: '/a.webp',
      priceFrom: 1,
      priceTo: 1,
      inStock: true,
      isFeatured: false,
    };

    /** Answers every pending catalog request with a 100-product catalog. */
    function answer(r: TestRequest): void {
      if (r.request.url.endsWith('/facets')) r.flush(facets);
      else
        r.flush({
          items: [item],
          page: Number(r.request.params.get('page')),
          pageSize: 24,
          totalCount: 100,
          totalPages: 5,
        });
    }

    async function respond(http: HttpTestingController, harness: RouterTestingHarness) {
      for (const r of http.match(() => true)) answer(r);
      await harness.fixture.whenStable();
    }

    async function open(url: string) {
      const ctx = await setup(url);
      await respond(ctx.http, ctx.harness);
      const router = TestBed.inject(Router);
      const el = ctx.harness.routeNativeElement as HTMLElement;
      // Navigation and resource requests are async: answer requests as they appear.
      const settle = async () => {
        for (let i = 0; i < 4; i++) {
          await new Promise((r) => setTimeout(r));
          ctx.harness.detectChanges();
          for (const r of ctx.http.match(() => true)) answer(r);
        }
        await ctx.harness.fixture.whenStable();
      };
      return { ...ctx, router, el, settle };
    }

    it('chip removal navigates without that filter', async () => {
      const { el, router, settle } = await open('/c/laptops?brand=Voltra&inStock=true&page=2');
      el.querySelector<HTMLButtonElement>('button[aria-label="Remove Voltra"]')!.click();
      await settle();
      expect(router.url).toBe('/c/laptops?inStock=true');
    });

    it('Clear all navigates to bare params', async () => {
      const { el, router, settle } = await open('/c/laptops?brand=Voltra&inStock=true&sort=priceAsc');
      const clear = Array.from(el.querySelectorAll('button')).find((b) =>
        b.textContent?.includes('Clear all'),
      )!;
      clear.click();
      await settle();
      expect(router.url).toBe('/c/laptops?sort=priceAsc');
    });

    it('filter change resets page to 1; paginator keeps the new page', async () => {
      const { el, router, settle } = await open('/c/laptops?page=3');
      el.querySelector<HTMLInputElement>('mat-checkbox[data-brand="Voltra"] input')!.click();
      await settle();
      expect(router.url).toBe('/c/laptops?brand=Voltra');
      el.querySelector<HTMLButtonElement>('.mat-mdc-paginator-navigation-next')!.click();
      await settle();
      expect(router.url).toBe('/c/laptops?brand=Voltra&page=2');
    });

    it('shows the invalid-filters state on a 400', async () => {
      const { http, harness } = await setup('/c/audio?spec=x:1..2');
      for (const r of http.match(() => true)) {
        if (r.request.url.endsWith('/facets')) r.flush({ ...facets, specs: [] });
        else
          r.flush(
            { type: 'validation.failed', status: 400, errors: { spec: ['Spec filters require a leaf category.'] } },
            { status: 400, statusText: 'Bad Request' },
          );
      }
      await harness.fixture.whenStable();
      const el = harness.routeNativeElement as HTMLElement;
      expect(el.textContent).toContain("Those filters can't be applied");
      expect(el.textContent).toContain('Clear filters');
    });

    it('offers a way back from an out-of-range page', async () => {
      const { http, harness } = await setup('/c/laptops?page=99');
      for (const r of http.match(() => true)) {
        if (r.request.url.endsWith('/facets')) r.flush(facets);
        else r.flush({ items: [], page: 99, pageSize: 24, totalCount: 100, totalPages: 5 });
      }
      await harness.fixture.whenStable();
      expect((harness.routeNativeElement as HTMLElement).textContent).toContain('This page is empty');
    });
  });
});
