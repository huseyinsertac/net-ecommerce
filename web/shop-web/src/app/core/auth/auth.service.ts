import { HttpErrorResponse } from '@angular/common/http';
import { Injectable, Signal, computed, inject, signal } from '@angular/core';
import { Observable, Subject, firstValueFrom } from 'rxjs';
import { AuthResponse, CurrentUser, IdentityApi } from '../../api/identity-api.service';

export type { CurrentUser } from '../../api/identity-api.service';

const STARTUP_TIMEOUT_MS = 5000;

/**
 * Session state. The access token lives only in memory; a reload restores the session through the
 * HttpOnly refresh cookie (see `initialize`, run by the app initializer).
 */
@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly api = inject(IdentityApi);

  private readonly _user = signal<CurrentUser | null>(null);
  private readonly _ready = signal(false);
  private readonly _signedIn = new Subject<CurrentUser>();
  private token: string | null = null;
  private inflight: Promise<boolean> | null = null;
  /** Bumped by every explicit sign-in and sign-out; a refresh started under an older value is stale. */
  private generation = 0;

  readonly user: Signal<CurrentUser | null> = this._user.asReadonly();
  readonly isAdmin = computed(() => this._user()?.roles.includes('Admin') ?? false);
  /** True once the initial silent refresh has finished, whatever its outcome. */
  readonly ready: Signal<boolean> = this._ready.asReadonly();

  /**
   * Emits the user when a sign-in changes who is signed in: null to a user (login, register, demo,
   * or a refresh that restores a session after reload), or one user replaced by another. It does
   * NOT emit for a token-only refresh of the user who is already signed in, nor on sign-out.
   */
  readonly signedIn: Observable<CurrentUser> = this._signedIn.asObservable();

  accessToken(): string | null {
    return this.token;
  }

  async login(email: string, password: string): Promise<void> {
    this.generation++;
    this.apply(await firstValueFrom(this.api.login(email, password)));
  }

  async register(email: string, password: string, displayName: string): Promise<void> {
    this.generation++;
    this.apply(await firstValueFrom(this.api.register(email, password, displayName)));
  }

  async startDemoCustomer(): Promise<void> {
    this.generation++;
    this.apply(await firstValueFrom(this.api.demoCustomer()));
  }

  async startDemoAdmin(): Promise<void> {
    this.generation++;
    this.apply(await firstValueFrom(this.api.demoAdmin()));
  }

  /** Clears local state even if the request fails (the server clears the cookie regardless). */
  async logout(): Promise<void> {
    this.generation++;
    try {
      await firstValueFrom(this.api.logout());
    } catch {
      // Local sign-out must not depend on the network.
    } finally {
      this.clear();
    }
  }

  /**
   * Rotates the session via the refresh cookie. Single-flight: concurrent callers share one request,
   * so two parallel 401s (or two components) never rotate the token twice and trip reuse detection.
   * Never rejects. Returns true when a new access token was obtained.
   *
   * 401 means the session is gone and clears local state. 429, 409, 5xx and network errors are
   * transient: the current state is kept so one hiccup does not sign the user out.
   */
  refresh(): Promise<boolean> {
    this.inflight ??= this.doRefresh().finally(() => (this.inflight = null));
    return this.inflight;
  }

  /** App initializer entry point: one silent refresh, then `ready`. Never rejects. */
  async initialize(): Promise<void> {
    let timer: ReturnType<typeof setTimeout> | undefined;
    try {
      // A hung refresh must not keep the app from rendering: give up after STARTUP_TIMEOUT_MS and
      // stay signed out. If the request completes later it still applies its result.
      await Promise.race([
        this.refresh(),
        new Promise<boolean>((resolve) => {
          timer = setTimeout(() => resolve(false), STARTUP_TIMEOUT_MS);
        }),
      ]);
    } finally {
      clearTimeout(timer);
      this._ready.set(true);
    }
  }

  private async doRefresh(): Promise<boolean> {
    const started = this.generation;
    try {
      const res = await firstValueFrom(this.api.refresh());
      // A sign-in or sign-out happened meanwhile: this result describes a dead session.
      if (started !== this.generation) return false;
      this.apply(res);
      return true;
    } catch (err) {
      if (started === this.generation && err instanceof HttpErrorResponse && err.status === 401) {
        this.clear();
      }
      return false;
    }
  }

  private apply(res: AuthResponse): void {
    const previous = this._user();
    this.token = res.accessToken;
    this._user.set(res.user);
    if (previous?.id !== res.user.id) this._signedIn.next(res.user);
  }

  private clear(): void {
    this.token = null;
    this._user.set(null);
  }
}
