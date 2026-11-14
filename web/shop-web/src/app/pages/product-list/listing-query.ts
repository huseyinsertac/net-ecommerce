import { ParamMap, Params } from '@angular/router';
import type { ProductQuery, ProductSort } from '../../api/catalog-api.service';
import { MoneyPipe } from '../../core/money.pipe';

export interface SpecSelection {
  min?: number;
  max?: number;
  values?: string[];
}

export interface ListingState {
  category?: string;
  q?: string;
  brands: string[];
  minPrice?: number;
  maxPrice?: number;
  inStock: boolean;
  specs: Record<string, SpecSelection>;
  sort?: ProductSort;
  page: number;
}

export const SORT_OPTIONS: readonly { value: ProductSort; label: string }[] = [
  { value: 'relevance', label: 'Relevance' },
  { value: 'featured', label: 'Featured' },
  { value: 'priceAsc', label: 'Price: low to high' },
  { value: 'priceDesc', label: 'Price: high to low' },
  { value: 'newest', label: 'Newest' },
];

const SORT_VALUES = new Set<string>(SORT_OPTIONS.map((o) => o.value));

/** Sort used when the URL names none: relevance for searches, featured otherwise. */
export function defaultSort(state: Pick<ListingState, 'q'>): ProductSort {
  return state.q ? 'relevance' : 'featured';
}

function num(raw: string | null | undefined, opts: { min?: number } = {}): number | undefined {
  if (raw === null || raw === undefined || raw.trim() === '') return undefined;
  const n = Number(raw);
  if (!Number.isFinite(n)) return undefined;
  if (opts.min !== undefined && n < opts.min) return undefined;
  return n;
}

function parseSpec(raw: string): [string, SpecSelection] | undefined {
  const colon = raw.indexOf(':');
  if (colon <= 0) return undefined;
  const key = raw.slice(0, colon).trim();
  const body = raw.slice(colon + 1);
  if (!key) return undefined;
  const dots = body.indexOf('..');
  if (dots >= 0) {
    const lo = body.slice(0, dots).trim();
    const hi = body.slice(dots + 2).trim();
    const min = lo ? num(lo) : undefined;
    const max = hi ? num(hi) : undefined;
    if ((lo && min === undefined) || (hi && max === undefined)) return undefined;
    if (min === undefined && max === undefined) return undefined;
    return [key, { ...(min !== undefined && { min }), ...(max !== undefined && { max }) }];
  }
  const values = body
    .split(',')
    .map((v) => v.trim())
    .filter(Boolean);
  return values.length ? [key, { values }] : undefined;
}

function serializeSpec(key: string, s: SpecSelection): string | undefined {
  if (s.values?.length) return `${key}:${s.values.join(',')}`;
  if (s.min !== undefined || s.max !== undefined) return `${key}:${s.min ?? ''}..${s.max ?? ''}`;
  return undefined;
}

function specStrings(specs: Record<string, SpecSelection>): string[] {
  return Object.entries(specs)
    .map(([k, s]) => serializeSpec(k, s))
    .filter((s): s is string => !!s);
}

/** Reads listing state from the URL. Malformed values are ignored rather than throwing. */
export function fromRoute(slug: string | undefined, params: ParamMap): ListingState {
  const q = params.get('q')?.trim() || undefined;
  const sortRaw = params.get('sort');
  const sort =
    sortRaw && SORT_VALUES.has(sortRaw) && (sortRaw !== 'relevance' || q)
      ? (sortRaw as ProductSort)
      : undefined;
  const pageRaw = num(params.get('page'));
  const specs: Record<string, SpecSelection> = {};
  for (const raw of params.getAll('spec')) {
    const parsed = parseSpec(raw);
    if (parsed) specs[parsed[0]] = parsed[1];
  }
  return {
    category: slug || undefined,
    q,
    brands: params.getAll('brand').filter(Boolean),
    minPrice: num(params.get('minPrice'), { min: 0 }),
    maxPrice: num(params.get('maxPrice'), { min: 0 }),
    inStock: params.get('inStock') === 'true',
    specs,
    sort,
    page: pageRaw !== undefined && Number.isInteger(pageRaw) && pageRaw >= 1 ? pageRaw : 1,
  };
}

/** URL query params for a state; defaults (page 1, inStock false, empties, default sort) are omitted. */
export function toQueryParams(state: ListingState): Params {
  const p: Params = {};
  if (state.q) p['q'] = state.q;
  if (state.brands.length) p['brand'] = state.brands;
  if (state.minPrice !== undefined) p['minPrice'] = state.minPrice;
  if (state.maxPrice !== undefined) p['maxPrice'] = state.maxPrice;
  if (state.inStock) p['inStock'] = 'true';
  const specs = specStrings(state.specs);
  if (specs.length) p['spec'] = specs;
  if (state.sort && state.sort !== defaultSort(state)) p['sort'] = state.sort;
  if (state.page > 1) p['page'] = state.page;
  return p;
}

export function toProductQuery(state: ListingState, pageSize: number): ProductQuery {
  return {
    category: state.category,
    q: state.q,
    brands: state.brands,
    minPrice: state.minPrice,
    maxPrice: state.maxPrice,
    inStock: state.inStock,
    specs: specStrings(state.specs),
    sort: state.sort ?? defaultSort(state),
    page: state.page,
    pageSize,
  };
}

/** Applies a filter/sort change; any such change returns to page 1. */
export function applyFilterChange(state: ListingState, patch: Partial<ListingState>): ListingState {
  return { ...state, ...patch, page: 1 };
}

/** Clears every filter but keeps the category and search text. */
export function clearFilters(state: ListingState): ListingState {
  return applyFilterChange(state, {
    brands: [],
    minPrice: undefined,
    maxPrice: undefined,
    inStock: false,
    specs: {},
  });
}

export interface FilterChip {
  id: string;
  label: string;
  /** The state with this one filter removed. */
  remove: (state: ListingState) => ListingState;
}

const money = new MoneyPipe();

function specText(sel: SpecSelection, unit: string): string | undefined {
  if (sel.values?.length) {
    return sel.values.map((v) => (v === 'true' ? 'Yes' : v === 'false' ? 'No' : v)).join(', ');
  }
  if (sel.min !== undefined && sel.max !== undefined) return `${sel.min}-${sel.max}${unit}`;
  if (sel.min !== undefined) return `${sel.min}+${unit}`;
  if (sel.max !== undefined) return `up to ${sel.max}${unit}`;
  return undefined;
}

/** One removable chip per active filter. `specMeta` supplies labels/units from the facets. */
export function activeChips(
  state: ListingState,
  specMeta: Record<string, { label: string; unit: string | null }>,
): FilterChip[] {
  const chips: FilterChip[] = state.brands.map((b) => ({
    id: `brand:${b}`,
    label: b,
    remove: (s) => applyFilterChange(s, { brands: s.brands.filter((x) => x !== b) }),
  }));
  const { minPrice: lo, maxPrice: hi } = state;
  if (lo !== undefined || hi !== undefined) {
    const label =
      lo !== undefined && hi !== undefined
        ? `${money.transform(lo)} - ${money.transform(hi)}`
        : lo !== undefined
          ? `From ${money.transform(lo)}`
          : `Up to ${money.transform(hi as number)}`;
    chips.push({
      id: 'price',
      label,
      remove: (s) => applyFilterChange(s, { minPrice: undefined, maxPrice: undefined }),
    });
  }
  if (state.inStock) {
    chips.push({
      id: 'inStock',
      label: 'In stock',
      remove: (s) => applyFilterChange(s, { inStock: false }),
    });
  }
  for (const [key, sel] of Object.entries(state.specs)) {
    const meta = specMeta[key] ?? { label: key, unit: null };
    const text = specText(sel, meta.unit ? ` ${meta.unit}` : '');
    if (text === undefined) continue;
    chips.push({
      id: `spec:${key}`,
      label: `${meta.label}: ${text}`,
      remove: (s) => {
        const rest = { ...s.specs };
        delete rest[key];
        return applyFilterChange(s, { specs: rest });
      },
    });
  }
  return chips;
}
