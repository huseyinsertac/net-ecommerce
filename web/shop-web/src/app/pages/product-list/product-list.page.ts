import { BreakpointObserver } from '@angular/cdk/layout';
import { NgTemplateOutlet } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import {
  ChangeDetectionStrategy,
  Component,
  computed,
  effect,
  inject,
  linkedSignal,
  signal,
  viewChild,
} from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { MatButtonModule } from '@angular/material/button';
import { MatChipsModule } from '@angular/material/chips';
import { MatIconModule } from '@angular/material/icon';
import { MatPaginatorModule, PageEvent } from '@angular/material/paginator';
import { MatSelectModule } from '@angular/material/select';
import { MatSidenav, MatSidenavModule } from '@angular/material/sidenav';
import { Title } from '@angular/platform-browser';
import { ActivatedRoute, Router } from '@angular/router';
import { combineLatest, map } from 'rxjs';
import { CatalogApi, PagedProducts } from '../../api/catalog-api.service';
import { NotFoundComponent } from '../../shared/not-found/not-found.component';
import { ProductCardComponent } from '../../shared/product-card/product-card.component';
import { FilterPanelComponent } from './filter-panel/filter-panel.component';
import {
  ListingState,
  SORT_OPTIONS,
  activeChips,
  applyFilterChange,
  clearFilters,
  defaultSort,
  fromRoute,
  toProductQuery,
  toQueryParams,
} from './listing-query';

const PAGE_SIZE = 24;

/** "chargers-power" -> "Chargers & power", shown until the real name arrives. */
function humanise(slug: string): string {
  const text = slug.split('-').filter(Boolean).join(' & ');
  return text.charAt(0).toUpperCase() + text.slice(1);
}

/** Product grid for a category (`/c/:slug`) or a search (`/search?q=`). State lives only in the URL. */
@Component({
  selector: 'app-product-list-page',
  imports: [
    MatButtonModule,
    MatChipsModule,
    MatIconModule,
    MatPaginatorModule,
    MatSelectModule,
    MatSidenavModule,
    FilterPanelComponent,
    NgTemplateOutlet,
    NotFoundComponent,
    ProductCardComponent,
  ],
  templateUrl: './product-list.page.html',
  styleUrl: './product-list.page.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ProductListPage {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly api = inject(CatalogApi);
  private readonly titleService = inject(Title);

  protected readonly pageSize = PAGE_SIZE;
  protected readonly skeletons = Array.from({ length: 12 }, (_, i) => i);

  protected readonly state = toSignal(
    combineLatest([this.route.paramMap, this.route.queryParamMap]).pipe(
      map(([p, q]) => fromRoute(p.get('slug') ?? undefined, q)),
    ),
    { requireSync: true },
  );

  private readonly categorySlug = computed(() => this.state().category);
  protected readonly facets = this.api.facets(this.categorySlug);
  private readonly categoryTree = this.api.categories();
  /** Category tree for the "Category" block; undefined until loaded (the block is simply absent). */
  protected readonly categories = computed(() =>
    this.categoryTree.hasValue() ? this.categoryTree.value() : undefined,
  );
  protected readonly products = this.api.products(computed(() => toProductQuery(this.state(), PAGE_SIZE)));

  protected readonly compact = toSignal(
    inject(BreakpointObserver)
      .observe('(max-width: 899.98px)')
      .pipe(map((r) => r.matches)),
    { initialValue: false },
  );
  protected readonly drawerTop = signal(64);
  private readonly drawer = viewChild(MatSidenav);
  protected readonly facetData = computed(() =>
    this.facets.hasValue() ? this.facets.value() : undefined,
  );

  protected readonly sortOptions = computed(() =>
    SORT_OPTIONS.filter((o) => o.value !== 'relevance' || this.state().q),
  );
  protected readonly sortValue = computed(() => this.state().sort ?? defaultSort(this.state()));

  private readonly status = computed(() => {
    for (const err of [this.products.error(), this.facets.error()]) {
      if (err instanceof HttpErrorResponse && (err.status === 404 || err.status === 400)) {
        return err.status;
      }
    }
    return undefined;
  });
  protected readonly notFound = computed(() => this.status() === 404);
  protected readonly invalidFilters = computed(() => this.status() === 400);
  protected readonly failed = computed(() => !this.status() && !!this.products.error());

  /** Last loaded page: kept while the next one loads so the grid does not flicker. */
  protected readonly page = linkedSignal<PagedProducts | undefined, PagedProducts | undefined>({
    source: () => (this.products.hasValue() ? this.products.value() : undefined),
    computation: (next, prev) => next ?? prev?.value,
  });
  protected readonly loading = computed(() => this.products.isLoading());
  protected readonly total = computed(() => this.page()?.totalCount);

  protected readonly title = computed(() => {
    const { q, category } = this.state();
    if (q) return `Results for "${q}"`;
    if (category) return this.facets.hasValue() ? this.facets.value().name : humanise(category);
    return 'All products';
  });

  protected readonly chips = computed(() => {
    const meta: Record<string, { label: string; unit: string | null }> = {};
    if (this.facets.hasValue()) {
      for (const s of this.facets.value().specs) meta[s.key] = { label: s.label, unit: s.unit };
    }
    return activeChips(this.state(), meta);
  });

  constructor() {
    // Category pages are named after the category; /search keeps its route title.
    effect(() => {
      if (this.state().category) this.titleService.setTitle(`${this.title()} · VoltLane`);
    });
  }

  protected change(patch: Partial<ListingState>): void {
    this.go(applyFilterChange(this.state(), patch));
  }

  protected removeChip(remove: (s: ListingState) => ListingState): void {
    this.go(remove(this.state()));
  }

  protected clearAll(): void {
    this.go(clearFilters(this.state()));
  }

  protected setSort(sort: ListingState['sort']): void {
    this.change({ sort });
  }

  protected firstPage(): void {
    this.go({ ...this.state(), page: 1 });
  }

  protected onPage(e: PageEvent): void {
    this.go({ ...this.state(), page: e.pageIndex + 1 });
    window.scrollTo({ top: 0 });
  }

  protected openFilters(): void {
    // The drawer sits below the sticky site header, whose height differs by breakpoint.
    const header = document.querySelector('app-header');
    if (header) this.drawerTop.set(Math.round(header.getBoundingClientRect().height));
    void this.drawer()?.open();
  }

  private go(next: ListingState): void {
    void this.router.navigate([], { queryParams: toQueryParams(next), queryParamsHandling: '' });
  }
}
