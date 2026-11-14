import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  computed,
  effect,
  inject,
  input,
  output,
  signal,
} from '@angular/core';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { ErrorStateMatcher } from '@angular/material/core';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { RouterLink } from '@angular/router';
import type { CategoryFacets, CategoryNode, SpecFacet } from '../../../api/catalog-api.service';
import { ListingState, SpecSelection } from '../listing-query';

const DEBOUNCE_MS = 400;

function toNumber(raw: string): number | undefined {
  if (raw.trim() === '') return undefined;
  const n = Number(raw);
  return Number.isFinite(n) && n >= 0 ? n : undefined;
}

export interface CategoryLink {
  slug: string;
  name: string;
  count?: number;
}

export interface CategoryBlock {
  /** Link back to the parent (leaf categories only). */
  up?: CategoryLink;
  items: CategoryLink[];
  /** Slug of the category being viewed, when it appears in `items`. */
  current?: string;
}

function findPath(nodes: CategoryNode[], slug: string): CategoryNode[] | undefined {
  for (const n of nodes) {
    if (n.slug === slug) return [n];
    const below = findPath(n.children, slug);
    if (below) return [n, ...below];
  }
  return undefined;
}

/** Presentational filter form: renders facets, emits partial state patches. */
@Component({
  selector: 'app-filter-panel',
  imports: [MatCheckboxModule, MatFormFieldModule, MatInputModule, MatSlideToggleModule, RouterLink],
  templateUrl: './filter-panel.component.html',
  styleUrl: './filter-panel.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class FilterPanelComponent {
  readonly facets = input<CategoryFacets>();
  readonly state = input.required<ListingState>();
  /** Category tree: drives the "Category" block so every category is reachable by clicking. */
  readonly categories = input<CategoryNode[]>();
  readonly filterChange = output<Partial<ListingState>>();
  /** Fired when a category link is followed (the mobile drawer closes on it). */
  readonly categoryPick = output<void>();

  /**
   * Search and top-level views list the top-level categories; a parent lists its children;
   * a leaf lists its siblings (current one marked) plus a link up to the parent.
   */
  protected readonly categoryBlock = computed<CategoryBlock | undefined>(() => {
    const tree = this.categories();
    if (!tree?.length) return undefined;
    const slug = this.state().category;
    // Counts are catalog-wide, so they would not match a text search's results: omit them there.
    const counted = !this.state().q;
    const link = (n: CategoryNode): CategoryLink => ({
      slug: n.slug,
      name: n.name,
      count: counted ? n.productCount : undefined,
    });
    const path = slug ? findPath(tree, slug) : undefined;
    if (!path) return { items: tree.map(link) };
    const node = path[path.length - 1];
    if (node.children.length) return { items: node.children.map(link) };
    const parent = path.length > 1 ? path[path.length - 2] : undefined;
    return {
      up: parent ? { slug: parent.slug, name: parent.name } : undefined,
      items: (parent?.children ?? tree).map(link),
      current: node.slug,
    };
  });

  /** Search text is kept across category links; every other filter is dropped. */
  protected readonly categoryQuery = computed(() => {
    const q = this.state().q;
    return q ? { q } : null;
  });

  /** Ids ('price', 'spec:<key>') whose min is above their max: nothing is emitted for them. */
  private readonly invalid = signal<ReadonlySet<string>>(new Set());
  private readonly matchers = new Map<string, ErrorStateMatcher>();

  protected rangeInvalid(id: string): boolean {
    return this.invalid().has(id);
  }

  protected matcher(id: string): ErrorStateMatcher {
    let m = this.matchers.get(id);
    if (!m) {
      m = { isErrorState: () => this.invalid().has(id) };
      this.matchers.set(id, m);
    }
    return m;
  }

  protected floor(n: number): number {
    return Math.floor(n);
  }

  protected ceil(n: number): number {
    return Math.ceil(n);
  }

  private readonly timers = new Map<string, ReturnType<typeof setTimeout>>();

  constructor() {
    inject(DestroyRef).onDestroy(() => this.cancelPending());
    // Any new URL state (chip removed, Clear all, back, another category) supersedes typed-but-unsent input.
    effect(() => {
      this.state();
      this.cancelPending();
    });
  }

  protected toggleBrand(brand: string, checked: boolean): void {
    const brands = this.state().brands.filter((b) => b !== brand);
    this.filterChange.emit({ brands: checked ? [...brands, brand] : brands });
  }

  protected setInStock(inStock: boolean): void {
    this.filterChange.emit({ inStock });
  }

  protected price(min: string, max: string, immediate = false): void {
    const lo = toNumber(min);
    const hi = toNumber(max);
    if (this.flagInverted('price', lo, hi)) return;
    this.run('price', immediate, () => this.filterChange.emit({ minPrice: lo, maxPrice: hi }));
  }

  protected specRange(key: string, min: string, max: string, immediate = false): void {
    const lo = toNumber(min);
    const hi = toNumber(max);
    const id = `spec:${key}`;
    if (this.flagInverted(id, lo, hi)) return;
    this.run(id, immediate, () => this.emitSpec(key, { min: lo, max: hi }));
  }

  /** Records whether min > max for `id`; when it is, drops any pending emit and returns true. */
  private flagInverted(id: string, lo: number | undefined, hi: number | undefined): boolean {
    const bad = lo !== undefined && hi !== undefined && lo > hi;
    if (bad !== this.invalid().has(id)) {
      const next = new Set(this.invalid());
      if (bad) next.add(id);
      else next.delete(id);
      this.invalid.set(next);
    }
    if (bad) {
      clearTimeout(this.timers.get(id));
      this.timers.delete(id);
    }
    return bad;
  }

  protected specHas(key: string, value: string): boolean {
    return this.state().specs[key]?.values?.includes(value) ?? false;
  }

  protected toggleSpecValue(key: string, value: string, checked: boolean): void {
    const current = this.state().specs[key]?.values ?? [];
    const rest = current.filter((v) => v !== value);
    this.emitSpec(key, { values: checked ? [...rest, value] : rest });
  }

  protected setBoolean(key: string, on: boolean): void {
    this.emitSpec(key, { values: on ? ['true'] : [] });
  }

  protected trueCount(spec: SpecFacet): number {
    return spec.options.find((o) => o.value === 'true')?.count ?? 0;
  }

  private emitSpec(key: string, sel: SpecSelection): void {
    const specs = { ...this.state().specs };
    const empty = !sel.values?.length && sel.min === undefined && sel.max === undefined;
    if (empty) delete specs[key];
    else specs[key] = sel.values ? { values: sel.values } : { min: sel.min, max: sel.max };
    this.filterChange.emit({ specs });
  }

  private cancelPending(): void {
    this.timers.forEach(clearTimeout);
    this.timers.clear();
  }

  private run(id: string, immediate: boolean, fn: () => void): void {
    clearTimeout(this.timers.get(id));
    this.timers.delete(id);
    if (immediate) fn();
    else this.timers.set(id, setTimeout(fn, DEBOUNCE_MS));
  }
}
