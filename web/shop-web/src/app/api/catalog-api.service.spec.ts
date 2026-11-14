import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { CatalogApi, ProductQuery, buildProductParams } from './catalog-api.service';

const empty: ProductQuery = { brands: [], inStock: false, specs: [], page: 1, pageSize: 24 };

describe('buildProductParams', () => {
  it('omits empty/default values', () => {
    expect(buildProductParams(empty).toString()).toBe('page=1&pageSize=24');
  });

  it('serializes every field and repeats arrays', () => {
    const p = buildProductParams({
      category: 'phones',
      q: 'volt',
      brands: ['A', 'B'],
      minPrice: 10,
      maxPrice: 99.5,
      inStock: true,
      featured: true,
      specs: ['ram:8..16', 'color:red,blue'],
      sort: 'priceAsc',
      page: 2,
      pageSize: 12,
    });
    expect(p.getAll('brand')).toEqual(['A', 'B']);
    expect(p.getAll('spec')).toEqual(['ram:8..16', 'color:red,blue']);
    expect(p.get('category')).toBe('phones');
    expect(p.get('q')).toBe('volt');
    expect(p.get('minPrice')).toBe('10');
    expect(p.get('maxPrice')).toBe('99.5');
    expect(p.get('inStock')).toBe('true');
    expect(p.get('featured')).toBe('true');
    expect(p.get('sort')).toBe('priceAsc');
    expect(p.get('page')).toBe('2');
    expect(p.get('pageSize')).toBe('12');
  });

  it('omits blank q and featured=false', () => {
    const p = buildProductParams({ ...empty, q: '', featured: false });
    expect(p.has('q')).toBe(false);
    expect(p.has('featured')).toBe(false);
  });
});

describe('CatalogApi', () => {
  it('requests the product by slug', async () => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    const api = TestBed.inject(CatalogApi);
    const ctrl = TestBed.inject(HttpTestingController);
    const ref = TestBed.runInInjectionContext(() => api.product(signal('volt-phone')));
    TestBed.tick();
    ctrl.expectOne('/api/catalog/products/volt-phone').flush({ slug: 'volt-phone' });
    await Promise.resolve();
    expect(ref.value()?.slug).toBe('volt-phone');
  });
});
