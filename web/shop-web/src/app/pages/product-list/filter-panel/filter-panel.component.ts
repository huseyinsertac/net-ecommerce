import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  effect,
  inject,
  input,
  output,
} from '@angular/core';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import type { CategoryFacets, SpecFacet } from '../../../api/catalog-api.service';
import { ListingState, SpecSelection } from '../listing-query';

const DEBOUNCE_MS = 400;

function toNumber(raw: string): number | undefined {
  if (raw.trim() === '') return undefined;
  const n = Number(raw);
  return Number.isFinite(n) && n >= 0 ? n : undefined;
}

/** Presentational filter form: renders facets, emits partial state patches. */
@Component({
  selector: 'app-filter-panel',
  imports: [MatCheckboxModule, MatFormFieldModule, MatInputModule, MatSlideToggleModule],
  templateUrl: './filter-panel.component.html',
  styleUrl: './filter-panel.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class FilterPanelComponent {
  readonly facets = input<CategoryFacets>();
  readonly state = input.required<ListingState>();
  readonly filterChange = output<Partial<ListingState>>();

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
    this.run('price', immediate, () =>
      this.filterChange.emit({ minPrice: toNumber(min), maxPrice: toNumber(max) }),
    );
  }

  protected specRange(key: string, min: string, max: string, immediate = false): void {
    this.run(`spec:${key}`, immediate, () =>
      this.emitSpec(key, { min: toNumber(min), max: toNumber(max) }),
    );
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
