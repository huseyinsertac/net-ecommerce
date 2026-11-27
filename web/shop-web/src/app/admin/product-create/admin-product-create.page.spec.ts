import { HttpErrorResponse } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { of, throwError } from 'rxjs';
import { AdminApi } from '../admin-api.service';
import { AdminProductCreatePage } from './admin-product-create.page';

const categories = [
  {
    id: 'c1',
    slug: 'phones',
    name: 'Phones',
    specSchema: [
      { key: 'screen', label: 'Screen size', type: 'number', unit: 'in', required: true, allowedValues: null },
      { key: 'chip', label: 'Chip', type: 'text', unit: null, required: false, allowedValues: null },
    ],
  },
];
const images = [
  { key: 'a', thumbnailUrl: '/a.jpg' },
  { key: 'b', thumbnailUrl: '/b.jpg' },
];

interface Internals {
  form: { patchValue(v: unknown): void; controls: Record<string, { setValue(v: unknown): void }> };
  specs: () => { controls: Record<string, { setValue(v: unknown): void }> } | null;
  axes: { controls: { patchValue(v: unknown): void }[] };
  addAxis(): void;
  variants: { controls: { patchValue(v: unknown): void; getRawValue(): unknown }[] };
  images: { set(v: string[]): void };
}

function setup(createProduct = vi.fn().mockReturnValue(of({ id: 'new1', slug: 'volt-x' }))) {
  const api = {
    categories: vi.fn().mockReturnValue(of(categories)),
    imageKeys: vi.fn().mockReturnValue(of(images)),
    createProduct,
  };
  TestBed.configureTestingModule({
    providers: [provideRouter([]), { provide: AdminApi, useValue: api }],
  });
  const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
  const fixture = TestBed.createComponent(AdminProductCreatePage);
  fixture.detectChanges();
  const el = fixture.nativeElement as HTMLElement;
  const internals = fixture.componentInstance as unknown as Internals;
  return { api, navigate, fixture, el, internals };
}

function fillBasics(i: Internals, fixture: { detectChanges(): void }) {
  i.form.patchValue({ category: 'phones', name: 'X', brand: 'Volt', description: 'Fast.' });
  fixture.detectChanges();
  i.specs()!.controls['screen'].setValue(6.1);
  i.images.set(['b', 'a']);
}

describe('AdminProductCreatePage', () => {
  it('builds the specs form from the chosen category', () => {
    const { el, internals, fixture } = setup();
    expect(el.querySelectorAll('app-spec-field').length).toBe(0);
    internals.form.patchValue({ category: 'phones' });
    fixture.detectChanges();
    expect(el.querySelectorAll('app-spec-field').length).toBe(2);
  });

  it('has one variant row when there are no axes', () => {
    const { el } = setup();
    expect(el.querySelectorAll('tr.variant-row').length).toBe(1);
  });

  it('generates a 2x2 grid for two axes with prefilled upper-case SKUs', () => {
    const { el, internals, fixture } = setup();
    internals.form.patchValue({ brand: 'Volt', name: 'X' });
    internals.addAxis();
    internals.addAxis();
    internals.axes.controls[0].patchValue({ name: 'Color', values: 'Black, Blue, Black,' });
    internals.axes.controls[1].patchValue({ name: 'Storage', values: '128 GB, 256 GB' });
    fixture.detectChanges();
    const rows = el.querySelectorAll('tr.variant-row');
    expect(rows.length).toBe(4);
    const skus = [...rows].map((r) => r.querySelector<HTMLInputElement>('input.sku')!.value);
    expect(skus).toEqual([
      'VOLT-X-BLACK-128-GB',
      'VOLT-X-BLACK-256-GB',
      'VOLT-X-BLUE-128-GB',
      'VOLT-X-BLUE-256-GB',
    ]);
  });

  it('submits the expected body and navigates to the editor', () => {
    const { api, navigate, internals, fixture, el } = setup();
    fillBasics(internals, fixture);
    internals.form.patchValue({ isFeatured: true });
    internals.addAxis();
    internals.addAxis();
    internals.axes.controls[0].patchValue({ name: 'Color', values: 'Black, Blue' });
    internals.axes.controls[1].patchValue({ name: 'Storage', values: '128 GB, 256 GB' });
    fixture.detectChanges();
    internals.variants.controls.forEach((c, n) => c.patchValue({ price: 500 + n, stock: 10 + n }));
    fixture.detectChanges();
    [...el.querySelectorAll('button')].find((b) => b.textContent?.trim() === 'Create product')!.click();
    expect(api.createProduct).toHaveBeenCalledWith({
      categorySlug: 'phones',
      name: 'X',
      brand: 'Volt',
      description: 'Fast.',
      isFeatured: true,
      imageKeys: ['b', 'a'],
      specs: { screen: 6.1 },
      optionAxes: [
        { name: 'Color', values: ['Black', 'Blue'] },
        { name: 'Storage', values: ['128 GB', '256 GB'] },
      ],
      variants: [
        { sku: 'VOLT-X-BLACK-128-GB', options: { Color: 'Black', Storage: '128 GB' }, price: 500, stockOnHand: 10 },
        { sku: 'VOLT-X-BLACK-256-GB', options: { Color: 'Black', Storage: '256 GB' }, price: 501, stockOnHand: 11 },
        { sku: 'VOLT-X-BLUE-128-GB', options: { Color: 'Blue', Storage: '128 GB' }, price: 502, stockOnHand: 12 },
        { sku: 'VOLT-X-BLUE-256-GB', options: { Color: 'Blue', Storage: '256 GB' }, price: 503, stockOnHand: 13 },
      ],
    });
    expect(navigate).toHaveBeenCalledWith(['/admin/products', 'new1']);
  });

  it('does not submit while required fields are missing', () => {
    const { api, el, fixture } = setup();
    [...el.querySelectorAll('button')].find((b) => b.textContent?.trim() === 'Create product')!.click();
    fixture.detectChanges();
    expect(api.createProduct).not.toHaveBeenCalled();
  });

  it('shows validation field messages from the server', () => {
    const bad = new HttpErrorResponse({
      status: 400,
      error: { type: 'validation.failed', title: 'Validation failed', errors: { sku: ['SKU already exists.'] } },
    });
    const { el, internals, fixture } = setup(vi.fn().mockReturnValue(throwError(() => bad)));
    fillBasics(internals, fixture);
    internals.variants.controls[0].patchValue({ price: 10, stock: 1 });
    fixture.detectChanges();
    [...el.querySelectorAll('button')].find((b) => b.textContent?.trim() === 'Create product')!.click();
    fixture.detectChanges();
    expect(el.textContent).toContain('SKU already exists.');
  });
});
