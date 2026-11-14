import { convertToParamMap } from '@angular/router';
import { ProductSort } from '../../api/catalog-api.service';
import {
  ListingState,
  activeChips,
  applyFilterChange,
  fromRoute,
  toProductQuery,
  toQueryParams,
} from './listing-query';

const params = (o: Record<string, string | string[]>) => convertToParamMap(o);

const full: ListingState = {
  category: 'laptops',
  q: 'volt book',
  brands: ['Voltra', 'Nimbus'],
  minPrice: 500,
  maxPrice: 1500.5,
  inStock: true,
  specs: { ram_gb: { min: 16 }, os: { values: ['Linux', 'Windows 11'] } },
  sort: 'priceAsc',
  page: 3,
};

describe('listing-query', () => {
  it('round trip: toQueryParams(fromRoute(...)) preserves state', () => {
    // The router stringifies query values; mimic that.
    const wire = Object.fromEntries(
      Object.entries(toQueryParams(full)).map(([k, v]) => [
        k,
        Array.isArray(v) ? v.map(String) : String(v),
      ]),
    );
    expect(fromRoute('laptops', params(wire))).toEqual(full);
  });

  it('defaults omitted from URL', () => {
    const empty: ListingState = { brands: [], inStock: false, specs: {}, page: 1 };
    expect(toQueryParams(empty)).toEqual({});
    expect(toQueryParams({ ...empty, sort: 'featured' })).toEqual({});
    expect(toQueryParams({ ...empty, q: 'x', sort: 'relevance' })).toEqual({ q: 'x' });
    expect(toQueryParams({ ...empty, q: 'x', sort: 'featured' })).toEqual({
      q: 'x',
      sort: 'featured',
    });
  });

  it('spec range and values serialize to "ram_gb:16..", "os:Linux,Windows 11"', () => {
    expect(toQueryParams(full)['spec']).toEqual(['ram_gb:16..', 'os:Linux,Windows 11']);
    const s = toQueryParams({ ...full, specs: { w: { max: 9 }, x: { min: 1, max: 2 } } });
    expect(s['spec']).toEqual(['w:..9', 'x:1..2']);
  });

  it('filter change resets page to 1', () => {
    expect(applyFilterChange(full, { inStock: false }).page).toBe(1);
    expect(applyFilterChange(full, { inStock: false }).inStock).toBe(false);
  });

  it('never crashes on malformed params', () => {
    const s = fromRoute(
      undefined,
      params({
        page: 'abc',
        minPrice: '-5',
        maxPrice: 'NaN',
        sort: 'bogus',
        spec: ['garbage', 'a:b..c', ':x', 'ok:1..2'],
        inStock: 'maybe',
      }),
    );
    expect(s.page).toBe(1);
    expect(s.minPrice).toBeUndefined();
    expect(s.maxPrice).toBeUndefined();
    expect(s.sort).toBeUndefined();
    expect(s.inStock).toBe(false);
    expect(s.specs).toEqual({ ok: { min: 1, max: 2 } });
    expect(fromRoute(undefined, params({ page: '0' })).page).toBe(1);
    expect(fromRoute(undefined, params({ page: '2.5' })).page).toBe(1);
  });

  it('ignores relevance sort without a query', () => {
    expect(fromRoute(undefined, params({ sort: 'relevance' })).sort).toBeUndefined();
    expect(fromRoute(undefined, params({ q: 'x', sort: 'relevance' })).sort).toBe('relevance');
  });

  it('toProductQuery sends the effective sort and raw spec filters', () => {
    const q = toProductQuery(full, 24);
    expect(q).toMatchObject({
      category: 'laptops',
      q: 'volt book',
      specs: ['ram_gb:16..', 'os:Linux,Windows 11'],
      sort: 'priceAsc' satisfies ProductSort,
      page: 3,
      pageSize: 24,
    });
    expect(toProductQuery({ ...full, sort: undefined, q: undefined }, 24).sort).toBe('featured');
    expect(toProductQuery({ ...full, sort: undefined }, 24).sort).toBe('relevance');
  });

  it('activeChips builds a removable chip per filter', () => {
    const chips = activeChips(full, {
      ram_gb: { label: 'Memory', unit: 'GB' },
      os: { label: 'OS', unit: null },
    });
    expect(chips.map((c) => c.label)).toEqual([
      'Voltra',
      'Nimbus',
      '$500.00 - $1,500.50',
      'In stock',
      'Memory: 16+ GB',
      'OS: Linux, Windows 11',
    ]);
    expect(chips[0].remove(full).brands).toEqual(['Nimbus']);
    expect(chips[4].remove(full).specs).toEqual({ os: full.specs['os'] });
  });
});
