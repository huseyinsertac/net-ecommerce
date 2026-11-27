import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

/** Thin wrapper over /api/payments. */
@Injectable({ providedIn: 'root' })
export class PaymentsApi {
  private readonly http = inject(HttpClient);

  /** Test mode only (no Stripe): reports the outcome picked on the simulated payment page. */
  simulate(orderId: string, succeed: boolean): Observable<void> {
    return this.http.post<void>(`/api/payments/simulated/${encodeURIComponent(orderId)}`, {
      succeed,
    });
  }
}
