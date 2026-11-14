import { ComponentFixture, TestBed } from '@angular/core/testing';
import { CategoryFacets } from '../../../api/catalog-api.service';
import { ListingState } from '../listing-query';
import { FilterPanelComponent } from './filter-panel.component';

const facets: CategoryFacets = {
  slug: 'laptops',
  name: 'Laptops',
  brands: [
    { value: 'Voltra', count: 4 },
    { value: 'Nimbus', count: 2 },
  ],
  minPrice: 399,
  maxPrice: 2499,
  specs: [
    { key: 'ram_gb', label: 'Memory', type: 'number', unit: 'GB', min: 8, max: 64, options: [] },
    {
      key: 'os',
      label: 'Operating system',
      type: 'enum',
      unit: null,
      min: null,
      max: null,
      options: [{ value: 'Linux', count: 1 }],
    },
    {
      key: 'touch',
      label: 'Touchscreen',
      type: 'boolean',
      unit: null,
      min: null,
      max: null,
      options: [
        { value: 'true', count: 2 },
        { value: 'false', count: 4 },
      ],
    },
  ],
};

const state: ListingState = { category: 'laptops', brands: [], inStock: false, specs: {}, page: 1 };

async function render(s: ListingState = state) {
  const f: ComponentFixture<FilterPanelComponent> = TestBed.createComponent(FilterPanelComponent);
  f.componentRef.setInput('facets', facets);
  f.componentRef.setInput('state', s);
  const emitted: Partial<ListingState>[] = [];
  f.componentInstance.filterChange.subscribe((e) => emitted.push(e));
  await f.whenStable();
  return { f, el: f.nativeElement as HTMLElement, emitted };
}

describe('FilterPanelComponent', () => {
  it('emits a brand toggle', async () => {
    const { el, emitted } = await render();
    const box = el.querySelector<HTMLInputElement>('mat-checkbox[data-brand="Voltra"] input')!;
    box.click();
    expect(emitted).toEqual([{ brands: ['Voltra'] }]);
  });

  it('unchecking a selected brand removes it', async () => {
    const { el, emitted } = await render({ ...state, brands: ['Voltra', 'Nimbus'] });
    el.querySelector<HTMLInputElement>('mat-checkbox[data-brand="Voltra"] input')!.click();
    expect(emitted).toEqual([{ brands: ['Nimbus'] }]);
  });

  it('renders a block per spec facet with unit and range', async () => {
    const { el } = await render();
    expect(el.textContent).toContain('Memory');
    expect(el.textContent).toContain('GB');
    expect(el.textContent).toContain('Operating system');
    expect(el.textContent).toContain('Touchscreen');
    expect(el.querySelector<HTMLInputElement>('input[data-spec-min="ram_gb"]')!.placeholder).toBe('8');
  });

  it('emits an enum spec selection', async () => {
    const { el, emitted } = await render();
    el.querySelector<HTMLInputElement>('mat-checkbox[data-spec="os"][data-value="Linux"] input')!.click();
    expect(emitted).toEqual([{ specs: { os: { values: ['Linux'] } } }]);
  });

  describe('debounced number inputs', () => {
    afterEach(() => vi.useRealTimers());

    function type(el: HTMLElement, selector: string, value: string): void {
      const input = el.querySelector<HTMLInputElement>(selector)!;
      input.value = value;
      input.dispatchEvent(new Event('input'));
    }

    it('typing then waiting 400 ms emits once', async () => {
      const { el, emitted } = await render();
      vi.useFakeTimers();
      type(el, 'input[data-price-min]', '500');
      vi.advanceTimersByTime(399);
      expect(emitted).toEqual([]);
      expect(emitted).toEqual([]);
      vi.advanceTimersByTime(1);
      vi.advanceTimersByTime(1000);
      expect(emitted).toEqual([{ minPrice: 500, maxPrice: undefined }]);
    });

    it('typing then changing state before 400 ms emits nothing', async () => {
      const { f, el, emitted } = await render();
      vi.useFakeTimers();
      type(el, 'input[data-spec-min="ram_gb"]', '16');
      f.componentRef.setInput('state', { ...state, inStock: true });
      f.detectChanges();
      vi.advanceTimersByTime(1000);
      expect(emitted).toEqual([]);
    });
  });
});
