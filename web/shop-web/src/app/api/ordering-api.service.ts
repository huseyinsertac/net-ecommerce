import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import type { components } from './schema';

type Schemas = components['schemas'];

export type CheckoutRequest = Schemas['CheckoutRequest'];
export type ShippingAddress = Schemas['ShippingAddressDto'];
export type CheckoutStatus = Schemas['CheckoutStatusDto'];
export type OrderDto = Schemas['OrderDto'];
export type OrderStatus = Schemas['OrderStatus'];
export type OrderSummary = Schemas['OrderSummaryDto'];
export type OrderSummaryPage = Schemas['PagedResultOfOrderSummaryDto'];

/** Thin wrapper over /api/ordering. */
@Injectable({ providedIn: 'root' })
export class OrderingApi {
  private readonly http = inject(HttpClient);

  /** The same `idempotencyKey` returns the same order, so a retry never double-places. */
  checkout(
    idempotencyKey: string,
    request: CheckoutRequest,
  ): Observable<{ orderId: string; orderNumber: string }> {
    return this.http.post<{ orderId: string; orderNumber: string }>(
      '/api/ordering/checkout',
      request,
      { headers: { 'Idempotency-Key': idempotencyKey } },
    );
  }

  checkoutStatus(orderId: string): Observable<CheckoutStatus> {
    return this.http.get<CheckoutStatus>(
      `/api/ordering/orders/${encodeURIComponent(orderId)}/checkout-status`,
    );
  }

  order(orderId: string): Observable<OrderDto> {
    return this.http.get<OrderDto>(`/api/ordering/orders/${encodeURIComponent(orderId)}`);
  }

  /** The signed-in shopper's orders, newest first. */
  orders(page: number, pageSize: number): Observable<OrderSummaryPage> {
    return this.http.get<OrderSummaryPage>('/api/ordering/orders', {
      params: { page, pageSize },
    });
  }
}
