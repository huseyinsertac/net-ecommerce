import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { CategoryFacets, CategoryNode } from '../../../api/catalog-api.service';
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

const node = (slug: string, name: string, productCount: number, children: CategoryNode[] = []): CategoryNode => ({
  slug,
  name,
  icon: 'x',
  imageUrl: '/x.webp',
  productCount,
  children,
});

const tree: CategoryNode[] = [
  node('audio', 'Audio', 9, [node('headphones', 'Headphones', 5), node('speakers', 'Speakers', 4)]),
  node('laptops', 'Laptops', 6),
];

const state: ListingState = { category: 'laptops', brands: [], inStock: false, specs: {}, page: 1 };

async function render(s: ListingState = state, categories: CategoryNode[] | undefined = undefined) {
  TestBed.configureTestingModule({ providers: [provideRouter([])] });
  const f: ComponentFixture<FilterPanelComponent> = TestBed.createComponent(FilterPanelComponent);
  f.componentRef.setInput('facets', facets);
  if (categories) f.componentRef.setInput('categories', categories);
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

  describe('category block', () => {
    const links = (el: HTMLElement) =>
      Array.from(el.querySelectorAll<HTMLAnchorElement>('nav[aria-label="Categories"] a')).map((a) => [
        a.getAttribute('href'),
        a.textContent?.replace(/\s+/g, ' ').trim(),
      ]);

    it('a parent lists its children as links with counts', async () => {
      const { el } = await render({ ...state, category: 'audio' }, tree);
      expect(links(el)).toEqual([
        ['/c/headphones', 'Headphones (5)'],
        ['/c/speakers', 'Speakers (4)'],
      ]);
    });

    it('a leaf lists its siblings, marks itself, and links up to the parent', async () => {
      const { el } = await render({ ...state, category: 'headphones' }, tree);
      expect(links(el)).toEqual([
        ['/c/audio', 'All Audio'],
        ['/c/speakers', 'Speakers (4)'],
      ]);
      expect(el.querySelector('[aria-current="page"]')?.textContent).toContain('Headphones');
    });

    it('search lists the top-level categories without catalog-wide counts and keeps q', async () => {
      const { el } = await render({ ...state, category: undefined, q: 'wireless' }, tree);
      expect(links(el)).toEqual([
        ['/c/audio?q=wireless', 'Audio'],
        ['/c/laptops?q=wireless', 'Laptops'],
      ]);
    });

    it('renders no block until the tree is known', async () => {
      const { el } = await render();
      expect(el.querySelector('nav[aria-label="Categories"]')).toBeNull();
    });
  });

  describe('price placeholders', () => {
    it('floor the min and ceil the max to whole dollars', async () => {
      TestBed.configureTestingModule({ providers: [provideRouter([])] });
      const f = TestBed.createComponent(FilterPanelComponent);
      f.componentRef.setInput('facets', { ...facets, minPrice: 399.5, maxPrice: 2199.9 });
      f.componentRef.setInput('state', state);
      await f.whenStable();
      const el = f.nativeElement as HTMLElement;
      expect(el.querySelector<HTMLInputElement>('input[data-price-min]')!.placeholder).toBe('399');
      expect(el.querySelector<HTMLInputElement>('input[data-price-max]')!.placeholder).toBe('2200');
    });
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

    it('min above max emits nothing and shows an inline error; fixing it emits', async () => {
      const { f, el, emitted } = await render();
      vi.useFakeTimers();
      type(el, 'input[data-price-max]', '100');
      vi.advanceTimersByTime(400);
      emitted.length = 0;
      type(el, 'input[data-price-min]', '500');
      vi.advanceTimersByTime(1000);
      f.detectChanges();
      expect(emitted).toEqual([]);
      expect(el.textContent).toContain('Min must be ≤ max');
      type(el, 'input[data-price-max]', '900');
      f.detectChanges();
      expect(el.textContent).not.toContain('Min must be ≤ max');
      vi.advanceTimersByTime(400);
      expect(emitted).toEqual([{ minPrice: 500, maxPrice: 900 }]);
    });

    it('a spec range with min above max emits nothing and shows an inline error', async () => {
      const { f, el, emitted } = await render();
      vi.useFakeTimers();
      type(el, 'input[data-spec-max="ram_gb"]', '8');
      vi.advanceTimersByTime(400);
      emitted.length = 0;
      type(el, 'input[data-spec-min="ram_gb"]', '32');
      vi.advanceTimersByTime(1000);
      f.detectChanges();
      expect(emitted).toEqual([]);
      expect(el.textContent).toContain('Min must be ≤ max');
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
