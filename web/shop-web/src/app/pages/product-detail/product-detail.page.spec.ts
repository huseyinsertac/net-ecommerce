import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { Title } from '@angular/platform-browser';
import { RouterTestingHarness } from '@angular/router/testing';
import { signal } from '@angular/core';
import { MatSnackBar } from '@angular/material/snack-bar';
import { HttpErrorResponse } from '@angular/common/http';
import { ProductDetail } from '../../api/catalog-api.service';
import { Basket, BasketService } from '../../core/basket/basket.service';
import { ProductDetailPage } from './product-detail.page';

const detail: ProductDetail = {
  id: 'p1',
  slug: 'aurora-x',
  name: 'Aurora X',
  brand: 'Voltra',
  description: 'A fine phone.',
  breadcrumb: [{ slug: 'phones', name: 'Phones' }],
  images: [
    { url: '/images/products/a.webp', thumbUrl: '/images/products/a-400.webp' },
    { url: '/images/products/b.webp', thumbUrl: '/images/products/b-400.webp' },
    { url: '/images/products/c.webp', thumbUrl: '/images/products/c-400.webp' },
  ],
  specs: [{ key: 'screen', label: 'Screen', value: '6.7 in' }],
  options: [
    { key: 'color', label: 'Colour', values: ['Black', 'Blue'] },
    { key: 'storage', label: 'Storage', values: ['128 GB', '256 GB', '512 GB'] },
  ],
  variants: [
    { id: '1', sku: 'BLK-128', options: { color: 'Black', storage: '128 GB' }, price: 500, available: 0 },
    { id: '2', sku: 'BLK-256', options: { color: 'Black', storage: '256 GB' }, price: 600, available: 3 },
    { id: '3', sku: 'BLU-128', options: { color: 'Blue', storage: '128 GB' }, price: 520, available: 20 },
  ],
};

function fakeBasket(lines: Basket['lines'] = []) {
  return {
    basket: signal<Basket | null>({ lines, itemCount: 0, subtotal: 0, hasIssues: false }),
    count: () => 0,
    add: vi.fn().mockResolvedValue(true),
  };
}

async function setup(url: string, body: object = detail, status = 200, basket = fakeBasket()) {
  TestBed.configureTestingModule({
    providers: [
      { provide: BasketService, useValue: basket },
      provideRouter([{ path: 'p/:slug', component: ProductDetailPage }]),
      provideHttpClient(),
      provideHttpClientTesting(),
    ],
  });
  const http = TestBed.inject(HttpTestingController);
  const harness = await RouterTestingHarness.create();
  await harness.navigateByUrl(url);
  http
    .expectOne('/api/catalog/products/aurora-x')
    .flush(body, status === 200 ? {} : { status, statusText: 'x' });
  await harness.fixture.whenStable();
  harness.detectChanges();
  return { harness, basket, el: harness.routeNativeElement as HTMLElement };
}

describe('ProductDetailPage', () => {
  it('sets the browser tab title to the product name', async () => {
    await setup('/p/aurora-x');
    expect(TestBed.inject(Title).getTitle()).toBe('Aurora X · VoltLane');
  });

  it('keeps the product tab title when a variant is selected', async () => {
    const { harness, el } = await setup('/p/aurora-x');
    const blue = Array.from(el.querySelectorAll<HTMLButtonElement>('mat-button-toggle button')).find(
      (b) => b.textContent?.trim() === 'Blue',
    )!;
    blue.click();
    await harness.fixture.whenStable();
    harness.detectChanges();
    expect(TestBed.inject(Router).url).toBe('/p/aurora-x?variant=BLU-128');
    expect(TestBed.inject(Title).getTitle()).toBe('Aurora X · VoltLane');
  });

  it('shows the selected variant price and stock label (first in-stock by default)', async () => {
    const { el } = await setup('/p/aurora-x');
    expect(el.querySelector('.price')?.textContent).toContain('$600.00');
    expect(el.textContent).toContain('Only 3 left');
    expect(el.textContent).toContain('Aurora X');
    expect(el.textContent).toContain('Screen');
    expect(el.textContent).toContain('Add to cart');
  });

  it('?variant= preselects that variant, including an out-of-stock one', async () => {
    const { el } = await setup('/p/aurora-x?variant=BLK-128');
    expect(el.querySelector('.price')?.textContent).toContain('$500.00');
    expect(el.textContent).toContain('Out of stock');
  });

  it('unknown ?variant= falls back and replaces the URL', async () => {
    const { harness } = await setup('/p/aurora-x?variant=NOPE');
    await harness.fixture.whenStable();
    expect(TestBed.inject(Router).url).toBe('/p/aurora-x?variant=BLK-256');
  });

  it('selecting a value updates the URL variant', async () => {
    const { harness, el } = await setup('/p/aurora-x');
    const blue = Array.from(el.querySelectorAll<HTMLButtonElement>('mat-button-toggle button')).find(
      (b) => b.textContent?.trim() === 'Blue',
    )!;
    blue.click();
    await harness.fixture.whenStable();
    harness.detectChanges();
    expect(TestBed.inject(Router).url).toBe('/p/aurora-x?variant=BLU-128');
    expect(el.querySelector('.price')?.textContent).toContain('$520.00');
  });

  it('disables values that no variant has, but keeps missing combinations clickable', async () => {
    const { el } = await setup('/p/aurora-x?variant=BLU-128');
    const toggles = Array.from(el.querySelectorAll('mat-button-toggle'));
    const named = (n: string) => toggles.find((t) => t.textContent?.trim() === n)!;
    expect(named('512 GB').classList).toContain('mat-button-toggle-disabled');
    expect(named('256 GB').classList).not.toContain('mat-button-toggle-disabled');
  });

  it('thumbnail click swaps the main image', async () => {
    const { harness, el } = await setup('/p/aurora-x');
    const main = () => el.querySelector<HTMLImageElement>('img.main')!;
    expect(main().getAttribute('src')).toBe('/images/products/a.webp');
    expect(main().alt).toContain('Aurora X');
    el.querySelector<HTMLButtonElement>('button[aria-label="Show image 2 of 3"]')!.click();
    harness.detectChanges();
    expect(main().getAttribute('src')).toBe('/images/products/b.webp');
  });

  it('arrow keys move through the gallery and wrap', async () => {
    const { harness, el } = await setup('/p/aurora-x');
    const gallery = el.querySelector<HTMLElement>('.gallery')!;
    gallery.dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowLeft', bubbles: true }));
    harness.detectChanges();
    expect(el.querySelector('img.main')!.getAttribute('src')).toBe('/images/products/c.webp');
    gallery.dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowRight', bubbles: true }));
    harness.detectChanges();
    expect(el.querySelector('img.main')!.getAttribute('src')).toBe('/images/products/a.webp');
  });

  it('renders NotFound on 404', async () => {
    const { el } = await setup(
      '/p/aurora-x',
      { type: 'catalog.product_not_found', status: 404 },
      404,
    );
    expect(el.textContent).toContain('Page not found');
  });

  it('shows no picker for a single-variant product', async () => {
    const single: ProductDetail = { ...detail, options: [], variants: [detail.variants[2]] };
    const { el } = await setup('/p/aurora-x', single);
    expect(el.querySelector('mat-button-toggle-group')).toBeNull();
    expect(el.querySelector('.price')?.textContent).toContain('$520.00');
  });

  describe('add to cart', () => {
    const addButton = (el: HTMLElement) => el.querySelector<HTMLButtonElement>('.cta button')!;
    const inCart = (variantId: string, quantity: number): Basket['lines'] => [
      {
        variantId,
        productSlug: 'aurora-x',
        productName: 'Aurora X',
        brand: 'Voltra',
        sku: 'BLU-128',
        optionsLabel: 'Blue',
        thumbnailUrl: null,
        unitPrice: 520,
        quantity,
        available: 20,
        lineTotal: 520 * quantity,
        issue: null,
      },
    ];

    it('Add to cart enabled for in-stock variant and calls add(variant, 1)', async () => {
      const { el, harness, basket } = await setup(
        '/p/aurora-x?variant=BLU-128',
        detail,
        200,
        fakeBasket(inCart('3', 2)),
      );
      const open = vi.spyOn(TestBed.inject(MatSnackBar), 'open');
      expect(addButton(el).disabled).toBe(false);
      expect(addButton(el).textContent).toContain('Add to cart');
      addButton(el).click();
      expect(basket.add).toHaveBeenCalledWith('3', 1);
      await harness.fixture.whenStable();
      expect(open).toHaveBeenCalledWith('Added to cart', 'View cart', expect.anything());
    });

    it('adds 1 when the variant is not in the cart yet', async () => {
      const { el, basket } = await setup('/p/aurora-x?variant=BLU-128');
      addButton(el).click();
      expect(basket.add).toHaveBeenCalledWith('3', 1);
    });

    it('is disabled while the request is in flight, so a double click sends once', async () => {
      let release!: () => void;
      const basket = fakeBasket();
      basket.add.mockImplementation(() => new Promise<void>((r) => (release = r)));
      const { el, harness } = await setup('/p/aurora-x?variant=BLU-128', detail, 200, basket);
      addButton(el).click();
      harness.detectChanges();
      expect(addButton(el).disabled).toBe(true);
      addButton(el).click();
      expect(basket.add).toHaveBeenCalledTimes(1);
      release();
      await harness.fixture.whenStable();
      harness.detectChanges();
      expect(addButton(el).disabled).toBe(false);
    });

    it('out-of-stock variant shows disabled Out of stock', async () => {
      const { el } = await setup('/p/aurora-x?variant=BLK-128');
      expect(addButton(el).disabled).toBe(true);
      expect(addButton(el).textContent).toContain('Out of stock');
    });

    it('shows the server message when stock runs short', async () => {
      const basket = fakeBasket();
      basket.add.mockRejectedValue(
        new HttpErrorResponse({ status: 409, error: { title: 'Only 3 left.' }, statusText: 'x' }),
      );
      const { el, harness } = await setup('/p/aurora-x?variant=BLK-256', detail, 200, basket);
      const open = vi.spyOn(TestBed.inject(MatSnackBar), 'open');
      addButton(el).click();
      await harness.fixture.whenStable();
      expect(open).toHaveBeenCalledWith('Only 3 left.', 'Dismiss', expect.anything());
    });
  });
});
