import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatSnackBar } from '@angular/material/snack-bar';
import { RouterLink } from '@angular/router';
import {
  BasketLine,
  BasketService,
  MAX_LINE_QUANTITY,
  basketErrorMessage,
} from '../../core/basket/basket.service';
import { MoneyPipe } from '../../core/money.pipe';

type View = 'loading' | 'error' | 'empty' | 'lines';

/** `/cart`. */
@Component({
  selector: 'app-cart-page',
  imports: [MatButtonModule, MatIconModule, RouterLink, MoneyPipe],
  templateUrl: './cart.page.html',
  styleUrl: './cart.page.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class CartPage {
  private readonly snackBar = inject(MatSnackBar);
  protected readonly cart = inject(BasketService);

  /** Quantities the shopper has asked for that the server has not confirmed yet, by variant. */
  private readonly pending = signal<Record<string, number>>({});

  protected readonly view = computed<View>(() => {
    const basket = this.cart.basket();
    if (basket === null) return this.cart.loadFailed() ? 'error' : 'loading';
    return basket.lines.length === 0 ? 'empty' : 'lines';
  });
  protected readonly skeletonRows = [0, 1, 2];

  constructor() {
    // Refresh prices and stock on every visit; any basket already in memory stays on screen meanwhile.
    void this.cart.load();
  }

  protected retry(): void {
    void this.cart.load();
  }

  /** What the stepper shows: the pending target if there is one, else the confirmed quantity. */
  protected shown(line: BasketLine): number {
    return this.pending()[line.variantId] ?? line.quantity;
  }

  /** Highest quantity the shopper can ask for on this line. */
  protected max(line: BasketLine): number {
    return Math.min(MAX_LINE_QUANTITY, line.available);
  }

  protected name(line: BasketLine): string {
    return line.productName ?? 'Unavailable item';
  }

  /**
   * Each click sends an absolute target quantity. The service serializes the requests, so rapid
   * clicks land in order and the last one wins; the number on screen follows the clicks at once.
   */
  protected async step(line: BasketLine, delta: number): Promise<void> {
    const current = this.shown(line);
    const target = Math.max(1, Math.min(current + delta, this.max(line)));
    if (target === current) return;
    this.setPending(line.variantId, target);
    try {
      await this.cart.setQuantity(line.variantId, target);
    } catch (err) {
      this.toast(err);
    } finally {
      // Only drop our own target; a later click may have replaced it and is still in flight.
      if (this.pending()[line.variantId] === target) this.setPending(line.variantId, undefined);
    }
  }

  protected async remove(line: BasketLine): Promise<void> {
    try {
      await this.cart.remove(line.variantId);
    } catch (err) {
      this.toast(err);
    }
  }

  private setPending(id: string, quantity: number | undefined): void {
    this.pending.update((p) => {
      const next = { ...p };
      if (quantity === undefined) delete next[id];
      else next[id] = quantity;
      return next;
    });
  }

  private toast(err: unknown): void {
    const message = basketErrorMessage(err);
    if (message) this.snackBar.open(message, 'Dismiss', { duration: 6000 });
  }
}
