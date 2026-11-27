import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import type { components } from '../api/schema';

type Schemas = components['schemas'];

export type AdminOrderDto = Schemas['AdminOrderDto'];
export type AdminOrderSummary = Schemas['AdminOrderSummaryDto'];
export type AdminOrderPage = Schemas['PagedResultOfAdminOrderSummaryDto'];

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
}
