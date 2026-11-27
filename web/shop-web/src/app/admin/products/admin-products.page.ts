import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  computed,
  effect,
  inject,
  input,
  signal,
  untracked,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { MatButtonModule } from '@angular/material/button';
import { MatChipsModule } from '@angular/material/chips';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatPaginatorModule, PageEvent } from '@angular/material/paginator';
import { MatTableModule } from '@angular/material/table';
import { Router, RouterLink } from '@angular/router';
import { Subject, Subscription, debounceTime, map } from 'rxjs';
import { MoneyPipe } from '../../core/money.pipe';
import { ProductStatusChipComponent } from '../product-status-chip/product-status-chip.component';
import { AdminApi, AdminProductPage, AdminProductQuery } from '../admin-api.service';

/** Routed inputs arrive as undefined when the query param is absent; fall back to empty. */
const orEmpty = (v: string | undefined): string => v ?? '';
const PAGE_SIZE = 20;
const SEARCH_DEBOUNCE_MS = 300;
const STATUSES = ['Draft', 'Active', 'Archived'];

/** `/admin/products`: the catalogue for the admin, filterable by status and searchable; the filters live in the URL. */
@Component({
  selector: 'app-admin-products-page',
  imports: [
    MatButtonModule,
    MatChipsModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    MatPaginatorModule,
    MatTableModule,
    MoneyPipe,
    ProductStatusChipComponent,
    RouterLink,
  ],
  templateUrl: './admin-products.page.html',
  styleUrls: ['../../shared/order-view.scss', './admin-products.page.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AdminProductsPage {
  private readonly api = inject(AdminApi);
  private readonly router = inject(Router);

  /** Query params, bound from the route. */
  readonly status = input('', { transform: orEmpty });
  readonly q = input('', { transform: orEmpty });
  readonly page = input('1', { transform: orEmpty });

  protected readonly statuses = STATUSES;
  protected readonly columns = ['name', 'category', 'status', 'variants', 'stock', 'price'];
  protected readonly pageSize = PAGE_SIZE;
  protected readonly skeletonRows = [1, 2, 3, 4, 5, 6];
  protected readonly view = signal<'loading' | 'error' | 'ready'>('loading');
  protected readonly result = signal<AdminProductPage | null>(null);
  protected readonly pageNumber = computed(() => Math.max(1, Number.parseInt(this.page(), 10) || 1));
  private readonly query = computed<AdminProductQuery>(() => ({
    status: this.status(),
    q: this.q(),
    page: this.pageNumber(),
    pageSize: PAGE_SIZE,
  }));
  private readonly search$ = new Subject<string>();
  private sub: Subscription | null = null;

  constructor() {
    inject(DestroyRef).onDestroy(() => this.sub?.unsubscribe());
    this.search$
      .pipe(
        debounceTime(SEARCH_DEBOUNCE_MS),
        map((v) => v.trim()),
        takeUntilDestroyed(),
      )
      .subscribe((q) => {
        if (q !== this.q()) this.update({ q: q || null, page: null });
      });
    effect(() => {
      const query = this.query();
      untracked(() => this.load(query));
    });
  }

  protected load(query: AdminProductQuery = this.query()): void {
    this.sub?.unsubscribe();
    this.view.set('loading');
    this.sub = this.api.products(query).subscribe({
      next: (result) => {
        this.result.set(result);
        this.view.set('ready');
      },
      error: () => this.view.set('error'),
    });
  }

  protected onSearch(event: Event): void {
    this.search$.next((event.target as HTMLInputElement).value);
  }

  protected onStatus(value: string | null | undefined): void {
    this.update({ status: value || null, page: null });
  }

  protected onPage(event: PageEvent): void {
    this.update({ page: event.pageIndex + 1 });
    window.scrollTo({ top: 0 });
  }

  protected clearFilters(): void {
    this.update({ status: null, q: null, page: null });
  }

  private update(queryParams: Record<string, string | number | null>): void {
    void this.router.navigate([], { queryParams, queryParamsHandling: 'merge' });
  }
}
