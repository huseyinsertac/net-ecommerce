import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, computed, effect, inject } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { Title } from '@angular/platform-browser';
import { MatButtonModule } from '@angular/material/button';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { combineLatest, map } from 'rxjs';
import { CatalogApi, ProductDetail } from '../../api/catalog-api.service';
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

  constructor() {
    effect(() => {
      const d = this.detail();
      if (d) this.titleService.setTitle(`${d.name} · VoltLane`);
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
}
