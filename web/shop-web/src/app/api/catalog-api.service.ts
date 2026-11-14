import {
  HttpParameterCodec,
  HttpParams,
  HttpResourceRef,
  httpResource,
} from '@angular/common/http';
import { Injectable, Signal } from '@angular/core';
import type { components, paths } from './schema';

type Schemas = components['schemas'];

export type ProductSummary = Schemas['ProductSummaryDto'];
export type ProductDetail = Schemas['ProductDetailDto'];
export type CategoryNode = Schemas['CategoryNodeDto'];
export type CategoryFacets = Schemas['CategoryFacetsDto'];
export type PagedProducts = Schemas['PagedResultOfProductSummaryDto'];
export type FacetOption = Schemas['FacetOptionDto'];
export type SpecFacet = Schemas['SpecFacetDto'];
export type ProductSort = NonNullable<
  paths['/api/catalog/products']['get']['parameters']['query']
>['sort'] &
  string;

/**
 * Strict RFC 3986 codec. Angular's default codec leaves `+` (and `:`, `,`) raw, and ASP.NET
 * decodes a raw `+` as a space, so `c++` would arrive as `c  `.
 */
class StrictParameterCodec implements HttpParameterCodec {
  encodeKey = (key: string): string => encodeURIComponent(key);
  encodeValue = (value: string): string => encodeURIComponent(value);
  decodeKey = (key: string): string => decodeURIComponent(key);
  decodeValue = (value: string): string => decodeURIComponent(value);
}

const codec = new StrictParameterCodec();

export interface ProductQuery {
  category?: string;
  q?: string;
  brands: string[];
  minPrice?: number;
  maxPrice?: number;
  inStock: boolean;
  featured?: boolean;
  /** Raw spec filters: `key:min..max` or `key:v1,v2`. */
  specs: string[];
  sort?: ProductSort;
  page: number;
  pageSize: number;
}

/** Serializes a ProductQuery to the API's query params, omitting empty/default values. */
export function buildProductParams(query: ProductQuery): HttpParams {
  let params = new HttpParams({ encoder: codec });
  if (query.category) params = params.set('category', query.category);
  if (query.q) params = params.set('q', query.q);
  for (const brand of query.brands) params = params.append('brand', brand);
  if (query.minPrice !== undefined) params = params.set('minPrice', query.minPrice);
  if (query.maxPrice !== undefined) params = params.set('maxPrice', query.maxPrice);
  if (query.inStock) params = params.set('inStock', true);
  if (query.featured) params = params.set('featured', true);
  for (const spec of query.specs) params = params.append('spec', spec);
  if (query.sort) params = params.set('sort', query.sort);
  params = params.set('page', query.page).set('pageSize', query.pageSize);
  return params;
}

@Injectable({ providedIn: 'root' })
export class CatalogApi {
  categories(): HttpResourceRef<CategoryNode[] | undefined> {
    return httpResource<CategoryNode[]>(() => '/api/catalog/categories');
  }

  facets(slug: Signal<string | undefined>): HttpResourceRef<CategoryFacets | undefined> {
    return httpResource<CategoryFacets>(() => {
      const s = slug();
      return s ? `/api/catalog/categories/${encodeURIComponent(s)}/facets` : undefined;
    });
  }

  products(query: Signal<ProductQuery>): HttpResourceRef<PagedProducts | undefined> {
    return httpResource<PagedProducts>(() => ({
      url: '/api/catalog/products',
      params: buildProductParams(query()),
    }));
  }

  product(slug: Signal<string>): HttpResourceRef<ProductDetail | undefined> {
    return httpResource<ProductDetail>(() => `/api/catalog/products/${encodeURIComponent(slug())}`);
  }
}
