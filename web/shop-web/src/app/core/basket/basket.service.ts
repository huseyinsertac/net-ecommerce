import { HttpErrorResponse } from '@angular/common/http';
import { Injectable, Signal, computed, effect, inject, signal, untracked } from '@angular/core';
import { Observable, firstValueFrom } from 'rxjs';
import { BasketApi, BasketDto } from '../../api/basket-api.service';
import { AuthService } from '../auth/auth.service';

export type Basket = BasketDto;
export type { BasketLineDto as BasketLine, BasketLineIssue } from '../../api/basket-api.service';

export const MAX_LINE_QUANTITY = 10;

/**
 * Message to show for a failed basket call, or null when the error interceptor already toasted it
 * (network and 5xx). 404/409 carry a ProblemDetails title written for the shopper ("Only 3 left.").
 */
export function basketErrorMessage(err: unknown): string | null {
  if (!(err instanceof HttpErrorResponse)) return "We couldn't update your cart.";
  if (err.status === 0 || err.status >= 500) return null;
  const title = (err.error as { title?: unknown } | null)?.title;
  return typeof title === 'string' && title ? title : "We couldn't update your cart.";
}

/**
 * The shopper's basket (anonymous cookie basket or signed-in user basket; the server decides).
 *
 * Every call goes through one promise chain, so requests reach the server one at a time and in the
 * order they were made. That matters twice over: two cookieless first writes in parallel would
 * create two anonymous baskets, and a slow response must never overwrite a newer one.
 */
@Injectable({ providedIn: 'root' })
export class BasketService {
  private readonly api = inject(BasketApi);
  private readonly auth = inject(AuthService);

  private readonly _basket = signal<Basket | null>(null);
  private readonly _loadFailed = signal(false);
  private tail: Promise<unknown> = Promise.resolve();

  /** Null until the first load finishes. */
  readonly basket: Signal<Basket | null> = this._basket.asReadonly();
  readonly count = computed(() => this._basket()?.itemCount ?? 0);
  /** True when the latest load failed; cleared by the next successful load. */
  readonly loadFailed: Signal<boolean> = this._loadFailed.asReadonly();

  constructor() {
    // Sign-in (or a session restored by reload): fold the anonymous cart into the user's cart.
    // The server makes merging idempotent, so firing on a restore is harmless.
    this.auth.signedIn.subscribe(() => void this.mergeAfterSignIn().catch(() => undefined));

    // Sign-out: the user's cart is no longer ours to show; reload so it becomes the anonymous cart.
    let wasSignedIn = false;
    effect(() => {
      const signedIn = this.auth.user() !== null;
      if (wasSignedIn && !signedIn) untracked(() => void this.load());
      wasSignedIn = signedIn;
    });
  }

  /** Never rejects; a failure sets `loadFailed` and keeps whatever basket is already shown. */
  load(): Promise<void> {
    return this.enqueue(async () => {
      try {
        this._basket.set(await firstValueFrom(this.api.get()));
        this._loadFailed.set(false);
      } catch {
        this._loadFailed.set(true);
      }
    });
  }

  /** Sets a line's quantity (0 removes it). Rejects with the HttpErrorResponse on failure. */
  setQuantity(variantId: string, quantity: number): Promise<void> {
    return this.mutate(this.api.setQuantity(variantId, quantity));
  }

  remove(variantId: string): Promise<void> {
    return this.mutate(this.api.remove(variantId));
  }

  async clear(): Promise<void> {
    await this.enqueue(async () => {
      await firstValueFrom(this.api.clear(), { defaultValue: undefined });
      this._basket.set({ lines: [], itemCount: 0, subtotal: 0, hasIssues: false });
    });
  }

  /** POST /merge, then show the merged basket. Rejects on failure. */
  mergeAfterSignIn(): Promise<void> {
    return this.mutate(this.api.merge());
  }

  private mutate(request: Observable<Basket>): Promise<void> {
    return this.enqueue(async () => {
      this._basket.set(await firstValueFrom(request));
      this._loadFailed.set(false);
    });
  }

  /** Runs `task` after everything queued before it, whether or not that succeeded. */
  private enqueue(task: () => Promise<void>): Promise<void> {
    const run = this.tail.then(task);
    this.tail = run.catch(() => undefined);
    return run;
  }
}
