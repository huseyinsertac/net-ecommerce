import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { HomeComponent } from './home.component';

const node = (slug: string, name: string, productCount: number) => ({
  slug,
  name,
  icon: 'x',
  imageUrl: `/images/categories/${slug}-400.webp`,
  productCount,
  children: [],
});

describe('HomeComponent', () => {
  it('renders category cards with counts from the API and a shop-all CTA', async () => {
    TestBed.configureTestingModule({
      providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()],
    });
    const http = TestBed.inject(HttpTestingController);
    const f = TestBed.createComponent(HomeComponent);
    f.detectChanges();
    http
      .expectOne('/api/catalog/categories')
      .flush([node('phones', 'Phones', 12), node('laptops', 'Laptops', 9)]);
    http.expectOne((r) => r.url === '/api/catalog/products').flush({
      items: [],
      page: 1,
      pageSize: 8,
      totalCount: 0,
      totalPages: 0,
    });
    await f.whenStable();
    const el = f.nativeElement as HTMLElement;
    const links = Array.from(el.querySelectorAll<HTMLAnchorElement>('a.category'));
    expect(links.map((a) => a.getAttribute('href'))).toEqual(['/c/phones', '/c/laptops']);
    expect(links[0].textContent).toContain('Phones');
    expect(links[0].textContent).toContain('12 products');
    expect(el.querySelector('a[href="/search"]')?.textContent).toContain('Shop all products');
  });

  it('requests 8 featured products', async () => {
    TestBed.configureTestingModule({
      providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()],
    });
    const http = TestBed.inject(HttpTestingController);
    const f = TestBed.createComponent(HomeComponent);
    f.detectChanges();
    const req = http.expectOne((r) => r.url === '/api/catalog/products');
    expect(req.request.params.get('featured')).toBe('true');
    expect(req.request.params.get('pageSize')).toBe('8');
  });
});
