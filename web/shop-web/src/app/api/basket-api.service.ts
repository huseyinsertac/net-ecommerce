import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import type { components } from './schema';

export type BasketDto = components['schemas']['BasketDto'];
export type BasketLineDto = components['schemas']['BasketLineDto'];
export type BasketLineIssue = components['schemas']['BasketLineIssue'];

/**
 * Thin wrapper over /api/basket. The anonymous `vl_basket` cookie is HttpOnly and same-origin:
 * the browser sends it and the server issues it, this code never touches it.
 */
@Injectable({ providedIn: 'root' })
export class BasketApi {
  private readonly http = inject(HttpClient);

  get(): Observable<BasketDto> {
    return this.http.get<BasketDto>('/api/basket');
  }

  setQuantity(variantId: string, quantity: number): Observable<BasketDto> {
    return this.http.put<BasketDto>(`/api/basket/items/${encodeURIComponent(variantId)}`, {
      quantity,
    });
  }

  remove(variantId: string): Observable<BasketDto> {
    return this.http.delete<BasketDto>(`/api/basket/items/${encodeURIComponent(variantId)}`);
  }

  clear(): Observable<void> {
    return this.http.delete<void>('/api/basket');
  }

  merge(): Observable<BasketDto> {
    return this.http.post<BasketDto>('/api/basket/merge', null);
  }
}
