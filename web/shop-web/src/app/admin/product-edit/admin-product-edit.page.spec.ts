import { HttpErrorResponse } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { NEVER, Subject, of, throwError } from 'rxjs';
import { AdminApi } from '../admin-api.service';
import { AdminProductEditPage } from './admin-product-edit.page';

const schema = [
  { key: 'screen', label: 'Screen size', type: 'number' as const, unit: 'in', required: true, allowedValues: null },
  { key: 'chip', label: 'Chip', type: 'text' as const, unit: null, required: false, allowedValues: null },
  { key: 'esim', label: 'eSIM', type: 'bool' as const, unit: null, required: false, allowedValues: null },
  { key: 'tier', label: 'Tier', type: 'enum' as const, unit: null, required: true, allowedValues: ['Basic', 'Pro'] },
];

const dto = (over: Record<string, unknown> = {}) => ({
  id: 'p1',
  slug: 'volt-phone',
  name: 'Volt Phone',
  brand: 'Volt',
  description: 'A phone.',
  categoryId: 'c1',
  categorySlug: 'phones',
  status: 'Active' as const,
  isFeatured: false,
  imageKeys: ['a', 'b'],
  specs: { screen: 6.1, chip: 'V1', esim: true, tier: 'Pro' },
  specSchema: schema,
  optionAxes: [{ key: 'color', label: 'Color', values: ['Black', 'Blue'] }],
  variants: [
    { id: 'v1', sku: 'VOLT-BLACK', options: { color: 'Black' }, price: 499, stockOnHand: 10, stockReserved: 4 },
    { id: 'v2', sku: 'VOLT-BLUE', options: { color: 'Blue' }, price: 509, stockOnHand: 8, stockReserved: 0 },
  ],
  version: 7,
  ...over,
});

const images = [
  { key: 'a', thumbnailUrl: '/a.jpg' },
  { key: 'b', thumbnailUrl: '/b.jpg' },
  { key: 'c', thumbnailUrl: '/c.jpg' },
];

function problem(status: number, type: string, title: string, errors?: Record<string, string[]>) {
  return new HttpErrorResponse({ status, error: { type, title, errors } });
}

function setup(over: Record<string, unknown> = {}) {
  const api = {
    product: vi.fn().mockReturnValue(of(dto())),
    imageKeys: vi.fn().mockReturnValue(of(images)),
    updateProduct: vi.fn(),
    updateVariant: vi.fn(),
    ...over,
  };
  TestBed.configureTestingModule({
    providers: [provideRouter([]), { provide: AdminApi, useValue: api }],
  });
  const fixture = TestBed.createComponent(AdminProductEditPage);
  fixture.componentRef.setInput('id', 'p1');
  fixture.detectChanges();
  const el = fixture.nativeElement as HTMLElement;
  const button = (text: string, scope: ParentNode = el) =>
    [...scope.querySelectorAll('button')].find((b) => b.textContent?.trim() === text)!;
  return { api, fixture, el, button };
}

function type(input: HTMLInputElement, value: string) {
  input.value = value;
  input.dispatchEvent(new Event('input'));
}

describe('AdminProductEditPage', () => {
  it('shows skeletons while loading', () => {
    const { el } = setup({ product: vi.fn().mockReturnValue(NEVER) });
    expect(el.querySelector('[aria-busy="true"]')).not.toBeNull();
  });

  it('renders spec fields by type, with the unit suffix', () => {
    const { el } = setup();
    const fields = el.querySelectorAll('app-spec-field');
    expect(fields.length).toBe(4);
    expect(fields[0].querySelector('input[type="number"]')).not.toBeNull();
    expect(fields[0].textContent).toContain('in');
    expect(fields[1].querySelector('input[type="text"]')).not.toBeNull();
    expect(fields[2].querySelector('mat-slide-toggle')).not.toBeNull();
    expect(fields[3].querySelector('mat-select')).not.toBeNull();
  });

  it('shows View in shop only when active', () => {
    const active = setup();
    const link = [...active.el.querySelectorAll('a')].find((a) => a.textContent?.includes('View in shop'))!;
    expect(link.getAttribute('href')).toBe('/p/volt-phone');
    TestBed.resetTestingModule();
    const draft = setup({ product: vi.fn().mockReturnValue(of(dto({ status: 'Draft' }))) });
    expect(draft.el.textContent).not.toContain('View in shop');
  });

  it('saves the details with version, specs and image order', () => {
    const updated = dto({ name: 'Volt Phone 2', version: 8 });
    const updateProduct = vi.fn().mockReturnValue(of(updated));
    const { api, el, button, fixture } = setup({ updateProduct });
    type(el.querySelector<HTMLInputElement>('input[name="name"]')!, 'Volt Phone 2');
    button('Save changes').click();
    expect(api.updateProduct).toHaveBeenCalledWith('p1', {
      name: 'Volt Phone 2',
      brand: 'Volt',
      description: 'A phone.',
      isFeatured: false,
      imageKeys: ['a', 'b'],
      specs: { screen: 6.1, chip: 'V1', esim: true, tier: 'Pro' },
      status: 'Active',
      version: 7,
    });
    fixture.detectChanges();
    // The returned DTO replaces local state: the next save carries the fresh version.
    button('Save changes').click();
    expect(updateProduct.mock.calls[1][1].version).toBe(8);
  });

  it('shows the title with Reload on a 409 and refetches', () => {
    const conflict = problem(409, 'catalog.concurrent_update', 'Someone else changed this product. Reload to see their changes.');
    const { api, el, button, fixture } = setup({
      updateProduct: vi.fn().mockReturnValue(throwError(() => conflict)),
    });
    button('Save changes').click();
    fixture.detectChanges();
    expect(el.textContent).toContain('Someone else changed this product.');
    api.product.mockReturnValue(of(dto({ name: 'Theirs', version: 9 })));
    button('Reload').click();
    fixture.detectChanges();
    expect(api.product).toHaveBeenCalledTimes(2);
    expect(el.querySelector<HTMLInputElement>('input[name="name"]')!.value).toBe('Theirs');
    expect(el.textContent).not.toContain('Someone else changed this product.');
  });

  it('shows validation field messages when present', () => {
    const bad = problem(400, 'validation.failed', 'Validation failed', { name: ['Name is too long.'] });
    const { el, button, fixture } = setup({
      updateProduct: vi.fn().mockReturnValue(throwError(() => bad)),
    });
    button('Save changes').click();
    fixture.detectChanges();
    expect(el.textContent).toContain('Name is too long.');
  });

  it('saves a variant row with price, stock and version', () => {
    const updateVariant = vi.fn().mockReturnValue(of(dto({ version: 8 })));
    const { api, el, button } = setup({ updateVariant });
    const row = el.querySelectorAll('tr.variant-row')[1];
    expect(row.textContent).toContain('VOLT-BLUE');
    expect(row.textContent).toContain('Color: Blue');
    type(row.querySelector<HTMLInputElement>('input.price')!, '519.5');
    type(row.querySelector<HTMLInputElement>('input.stock')!, '12');
    button('Save', row).click();
    expect(api.updateVariant).toHaveBeenCalledWith('p1', 'v2', {
      price: 519.5,
      stockOnHand: 12,
      version: 7,
    });
  });

  it('shows the reserved count and an inline stock_below_reserved error on that row', () => {
    const err = problem(409, 'catalog.stock_below_reserved', 'VOLT-BLACK has 4 reserved; stock cannot go below that.');
    const { el, button, fixture } = setup({
      updateVariant: vi.fn().mockReturnValue(throwError(() => err)),
    });
    const rows = el.querySelectorAll('tr.variant-row');
    expect(rows[0].querySelector('.reserved')!.textContent).toContain('4');
    type(rows[0].querySelector<HTMLInputElement>('input.stock')!, '2');
    button('Save', rows[0]).click();
    fixture.detectChanges();
    expect(rows[0].textContent).toContain('VOLT-BLACK has 4 reserved');
    expect(rows[1].textContent).not.toContain('reserved;');
  });

  it('shows a not-found state', () => {
    const { el } = setup({
      product: vi.fn().mockReturnValue(throwError(() => new HttpErrorResponse({ status: 404 }))),
    });
    expect(el.textContent).toContain('Product not found');
  });

  it('keeps unsaved edits in other rows when one row saves, and uses the new version next', () => {
    const updateVariant = vi.fn().mockReturnValue(of(dto({ version: 8 })));
    const { el, button, fixture } = setup({ updateVariant });
    const rows = el.querySelectorAll('tr.variant-row');
    type(rows[0].querySelector<HTMLInputElement>('input.price')!, '600');
    type(rows[1].querySelector<HTMLInputElement>('input.price')!, '520');
    button('Save', rows[1]).click();
    fixture.detectChanges();
    expect(rows[0].querySelector<HTMLInputElement>('input.price')!.value).toBe('600');
    button('Save', rows[0]).click();
    expect(updateVariant.mock.calls[1][2]).toEqual({ price: 600, stockOnHand: 10, version: 8 });
  });

  it('keeps unsaved detail edits when a variant row saves', () => {
    const updateVariant = vi.fn().mockReturnValue(of(dto({ version: 8 })));
    const { el, button, fixture } = setup({ updateVariant });
    type(el.querySelector<HTMLTextAreaElement>('textarea[name="description"]') as unknown as HTMLInputElement, 'Mine');
    button('Save', el.querySelectorAll('tr.variant-row')[0]).click();
    fixture.detectChanges();
    expect(el.querySelector<HTMLTextAreaElement>('textarea[name="description"]')!.value).toBe('Mine');
  });

  it('disables every Save button while one save is pending', () => {
    const pending = new Subject<unknown>();
    const { el, button, fixture } = setup({ updateProduct: vi.fn().mockReturnValue(pending) });
    button('Save changes').click();
    fixture.detectChanges();
    const rowButtons = [...el.querySelectorAll('tr.variant-row')].map((r) => button('Save', r));
    expect(rowButtons.every((b) => b.disabled)).toBe(true);
    expect(el.querySelector<HTMLButtonElement>('button[type="submit"]')!.disabled).toBe(true);
  });

  it('rejects a price with more than two decimals and an over-long description', () => {
    const updateVariant = vi.fn();
    const updateProduct = vi.fn();
    const { el, button, fixture } = setup({ updateVariant, updateProduct });
    const row = el.querySelectorAll('tr.variant-row')[0];
    type(row.querySelector<HTMLInputElement>('input.price')!, '10.999');
    button('Save', row).click();
    expect(updateVariant).not.toHaveBeenCalled();
    type(el.querySelector<HTMLTextAreaElement>('textarea[name="description"]') as unknown as HTMLInputElement, 'x'.repeat(4001));
    button('Save changes').click();
    fixture.detectChanges();
    expect(updateProduct).not.toHaveBeenCalled();
  });
});
