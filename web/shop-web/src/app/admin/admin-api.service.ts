import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import type { components } from '../api/schema';

type Schemas = components['schemas'];

export type AdminOrderDto = Schemas['AdminOrderDto'];
export type AdminOrderSummary = Schemas['AdminOrderSummaryDto'];
export type AdminOrderPage = Schemas['PagedResultOfAdminOrderSummaryDto'];

export type AdminProductDto = Schemas['AdminProductDto'];
export type AdminProductSummary = Schemas['AdminProductSummaryDto'];
export type AdminProductPage = Schemas['PagedResultOfAdminProductSummaryDto'];
export type AdminProductStatus = Schemas['AdminProductStatus'];
export type AdminCategory = Schemas['AdminCategoryDto'];
export type AdminSpecDefinition = Schemas['AdminSpecDefinitionDto'];
export type AdminImageKey = Schemas['AdminImageKeyDto'];
export type CreateProductRequest = Schemas['CreateProductRequest'];
export type UpdateProductRequest = Schemas['UpdateProductRequest'];
export type UpdateVariantRequest = Schemas['UpdateVariantRequest'];
export type CreatedProduct = Schemas['CreatedProductDto'];

export interface AdminProductQuery {
  /** A product status name, or empty for all. */
  status: string;
  /** Name, brand or SKU fragment; empty for none. */
  q: string;
  page: number;
  pageSize: number;
}

export interface AdminOrderQuery {
  /** An OrderStatus name, or empty for all. */
  status: string;
  /** Order number or customer email fragment; empty for none. */
  q: string;
  page: number;
  pageSize: number;
}

/** Thin wrapper over /api/ordering/admin (Admin role required). */
@Injectable({ providedIn: 'root' })
export class AdminApi {
  private readonly http = inject(HttpClient);

  orders(query: AdminOrderQuery): Observable<AdminOrderPage> {
    let params = new HttpParams().set('page', query.page).set('pageSize', query.pageSize);
    if (query.status) params = params.set('status', query.status);
    if (query.q) params = params.set('q', query.q);
    return this.http.get<AdminOrderPage>('/api/ordering/admin/orders', { params });
  }

  order(id: string): Observable<AdminOrderDto> {
    return this.http.get<AdminOrderDto>(`/api/ordering/admin/orders/${encodeURIComponent(id)}`);
  }

  ship(id: string): Observable<AdminOrderDto> {
    return this.http.post<AdminOrderDto>(
      `/api/ordering/admin/orders/${encodeURIComponent(id)}/ship`,
      null,
    );
  }

  deliver(id: string): Observable<AdminOrderDto> {
    return this.http.post<AdminOrderDto>(
      `/api/ordering/admin/orders/${encodeURIComponent(id)}/deliver`,
      null,
    );
  }

  products(query: AdminProductQuery): Observable<AdminProductPage> {
    let params = new HttpParams().set('page', query.page).set('pageSize', query.pageSize);
    if (query.status) params = params.set('status', query.status);
    if (query.q) params = params.set('q', query.q);
    return this.http.get<AdminProductPage>('/api/catalog/admin/products', { params });
  }

  product(id: string): Observable<AdminProductDto> {
    return this.http.get<AdminProductDto>(`/api/catalog/admin/products/${encodeURIComponent(id)}`);
  }

  createProduct(body: CreateProductRequest): Observable<CreatedProduct> {
    return this.http.post<CreatedProduct>('/api/catalog/admin/products', body);
  }

  updateProduct(id: string, body: UpdateProductRequest): Observable<AdminProductDto> {
    return this.http.put<AdminProductDto>(
      `/api/catalog/admin/products/${encodeURIComponent(id)}`,
      body,
    );
  }

  updateVariant(
    id: string,
    variantId: string,
    body: UpdateVariantRequest,
  ): Observable<AdminProductDto> {
    return this.http.put<AdminProductDto>(
      `/api/catalog/admin/products/${encodeURIComponent(id)}/variants/${encodeURIComponent(variantId)}`,
      body,
    );
  }

  imageKeys(): Observable<AdminImageKey[]> {
    return this.http.get<AdminImageKey[]>('/api/catalog/admin/image-keys');
  }

  categories(): Observable<AdminCategory[]> {
    return this.http.get<AdminCategory[]>('/api/catalog/admin/categories');
  }
}
