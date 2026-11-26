import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import type { components } from './schema';

type Schemas = components['schemas'];

export type CurrentUser = Schemas['UserDto'];
export type AuthResponse = Schemas['AuthResponse'];

/** Thin wrapper over /api/identity. The refresh cookie (HttpOnly, same-origin) is never touched here. */
@Injectable({ providedIn: 'root' })
export class IdentityApi {
  private readonly http = inject(HttpClient);

  register(email: string, password: string, displayName: string): Observable<AuthResponse> {
    return this.http.post<AuthResponse>('/api/identity/register', { email, password, displayName });
  }

  login(email: string, password: string): Observable<AuthResponse> {
    return this.http.post<AuthResponse>('/api/identity/login', { email, password });
  }

  /** Null on 204: no refresh cookie was sent, so there is no session to restore. */
  refresh(): Observable<AuthResponse | null> {
    return this.http.post<AuthResponse | null>('/api/identity/refresh', null);
  }

  logout(): Observable<void> {
    return this.http.post<void>('/api/identity/logout', null);
  }

  demoCustomer(): Observable<AuthResponse> {
    return this.http.post<AuthResponse>('/api/identity/demo/customer', null);
  }

  demoAdmin(): Observable<AuthResponse> {
    return this.http.post<AuthResponse>('/api/identity/demo/admin', null);
  }
}
