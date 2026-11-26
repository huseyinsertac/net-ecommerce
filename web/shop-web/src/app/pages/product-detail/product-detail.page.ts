import { HttpErrorResponse } from '@angular/common/http';
import {
  ChangeDetectionStrategy,
  Component,
  computed,
  effect,
  inject,
  signal,
} from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { Title } from '@angular/platform-browser';
import { MatButtonModule } from '@angular/material/button';
import { MatSnackBar } from '@angular/material/snack-bar';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { combineLatest, map } from 'rxjs';
import { CatalogApi, ProductDetail } from '../../api/catalog-api.service';
import {
  BasketService,
  MAX_LINE_QUANTITY,
  basketErrorMessage,
} from '../../core/basket/basket.service';
import { MoneyPipe } from '../../core/money.pipe';
import { NotFoundComponent } from '../../shared/not-found/not-found.component';
import { StockBadgeComponent } from '../../shared/stock-badge/stock-badge.component';
import { GalleryComponent } from './gallery/gallery.component';
import { VariantPickerComponent } from './variant-picker/variant-picker.component';
import { Selection, initialSelection, resolveVariant } from './variant-logic';

/** "aurora-x-pro" -> "Aurora x pro", shown until the real name arrives. */
function humanise(slug: string): string {
  const text = slug.split('-').filter(Boolean).join(' ');
  return text.charAt(0).toUpperCase() + text.slice(1);
}

/** `/p/:slug?variant=<sku>`. The selected variant lives in the URL so links are shareable. */
@Component({
  selector: 'app-product-detail-page',
  imports: [
    MatButtonModule,
    RouterLink,
    MoneyPipe,
    GalleryComponent,
    NotFoundComponent,
    StockBadgeComponent,
    VariantPickerComponent,
  ],
  templateUrl: './product-detail.page.html',
  styleUrl: './product-detail.page.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ProductDetailPage {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly titleService = inject(Title);
  private readonly basket = inject(BasketService);
  private readonly snackBar = inject(MatSnackBar);

  private readonly params = toSignal(
    combineLatest([this.route.paramMap, this.route.queryParamMap]).pipe(
      map(([p, q]) => ({ slug: p.get('slug') ?? '', sku: q.get('variant') })),
    ),
    { requireSync: true },
  );
  private readonly slug = computed(() => this.params().slug);
  protected readonly product = inject(CatalogApi).product(this.slug);

  protected readonly detail = computed<ProductDetail | undefined>(() =>
    this.product.hasValue() ? this.product.value() : undefined,
  );
  protected readonly notFound = computed(() => {
    const err = this.product.error();
    return err instanceof HttpErrorResponse && err.status === 404;
  });
  protected readonly failed = computed(() => !!this.product.error() && !this.notFound());
  protected readonly fallbackName = computed(() => humanise(this.slug()));

  /** Variant named by `?variant=`, else the default (first in stock). */
  protected readonly variant = computed(() => {
    const d = this.detail();
    if (!d) return undefined;
    const fromUrl = d.variants.find((v) => v.sku === this.params().sku);
    return fromUrl ?? resolveVariant(d.variants, initialSelection(d));
  });
  protected readonly selection = computed<Selection>(() => this.variant()?.options ?? {});
  protected readonly hasPicker = computed(() => (this.detail()?.options.length ?? 0) > 0);

  /** True while an add is in flight: the button is disabled so a double click cannot send twice. */
  protected readonly adding = signal(false);
  protected readonly inStock = computed(() => (this.variant()?.available ?? 0) > 0);

  constructor() {
    effect(() => {
      const d = this.detail();
      if (d) this.titleService.setTitle(`${d.name} · VoltLane`);
      else if (this.notFound()) this.titleService.setTitle('Page not found · VoltLane');
      else this.titleService.setTitle(`${this.fallbackName()} · VoltLane`);
    });
    // A `?variant=` that names no variant of this product is replaced with the default one.
    effect(() => {
      const d = this.detail();
      const { sku } = this.params();
      const v = this.variant();
      if (d && v && sku !== null && v.sku !== sku) this.setSku(v.sku);
    });
  }

  protected choose(selection: Selection): void {
    const d = this.detail();
    const v = d && resolveVariant(d.variants, selection);
    if (v) this.setSku(v.sku);
  }

  private setSku(sku: string): void {
    void this.router.navigate([], {
      queryParams: { variant: sku },
      queryParamsHandling: 'merge',
      replaceUrl: true,
    });
  }

  /** Adds one of the selected variant: the cart quantity plus 1, capped at the per-line maximum. */
  protected async addToCart(): Promise<void> {
    const v = this.variant();
    if (!v || v.available <= 0 || this.adding()) return;
    this.adding.set(true);
    try {
      // Absolute quantities need the real current quantity, so wait for the first load if needed.
      if (this.basket.basket() === null) await this.basket.load();
      const current = this.basket.basket()?.lines.find((l) => l.variantId === v.id)?.quantity ?? 0;
      if (current >= MAX_LINE_QUANTITY) {
        this.snackBar.open(`You can add up to ${MAX_LINE_QUANTITY} of each item.`, 'Dismiss', {
          duration: 5000,
        });
        return;
      }
      await this.basket.setQuantity(v.id, current + 1);
      this.snackBar
        .open('Added to cart', 'View cart', { duration: 5000 })
        .onAction()
        .subscribe(() => void this.router.navigateByUrl('/cart'));
    } catch (err) {
      const message = basketErrorMessage(err);
      if (message) this.snackBar.open(message, 'Dismiss', { duration: 6000 });
    } finally {
      this.adding.set(false);
    }
  }
}
