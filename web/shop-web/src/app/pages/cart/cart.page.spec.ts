import { HttpErrorResponse } from '@angular/common/http';
import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { MatSnackBar } from '@angular/material/snack-bar';
import { provideRouter } from '@angular/router';
import { Basket, BasketService } from '../../core/basket/basket.service';
import { CartPage } from './cart.page';

type Line = Basket['lines'][number];

const line = (over: Partial<Line> = {}): Line => ({
  variantId: 'v1',
  productSlug: 'aurora-x',
  productName: 'Aurora X',
  brand: 'Voltra',
  sku: 'BLK-128',
  optionsLabel: 'Black / 128 GB',
  thumbnailUrl: '/images/products/a-400.webp',
  unitPrice: 100,
  quantity: 2,
  available: 5,
  lineTotal: 200,
  issue: null,
  ...over,
});

const basketOf = (...lines: Line[]): Basket => ({
  lines,
  itemCount: lines.reduce((n, l) => n + l.quantity, 0),
  subtotal: lines.reduce((n, l) => n + (l.lineTotal ?? 0), 0),
  hasIssues: lines.some((l) => l.issue),
});

function setup(initial: Basket | null) {
  const basket = signal<Basket | null>(initial);
  const fake = {
    basket,
    count: () => basket()?.itemCount ?? 0,
    loadFailed: signal(false),
    load: vi.fn().mockResolvedValue(undefined),
    setQuantity: vi.fn().mockResolvedValue(undefined),
    remove: vi.fn().mockResolvedValue(undefined),
    clear: vi.fn().mockResolvedValue(undefined),
  };
  TestBed.configureTestingModule({
    providers: [provideRouter([]), { provide: BasketService, useValue: fake }],
  });
  const fixture = TestBed.createComponent(CartPage);
  return { fixture, fake, el: fixture.nativeElement as HTMLElement };
}

describe('CartPage', () => {
  it('renders lines and subtotal', async () => {
    const { fixture, el, fake } = setup(
      basketOf(
        line(),
        line({ variantId: 'v2', productName: 'Pulse Buds', quantity: 1, unitPrice: 50, lineTotal: 50 }),
      ),
    );
    await fixture.whenStable();
    expect(fake.load).toHaveBeenCalled();
    expect(el.querySelectorAll('.line').length).toBe(2);
    expect(el.textContent).toContain('Aurora X');
    expect(el.textContent).toContain('Black / 128 GB');
    expect(el.textContent).toContain('$100.00');
    expect(el.querySelector('.line-total')?.textContent).toContain('$200.00');
    expect(el.querySelector('.summary')?.textContent).toContain('$250.00');
    expect(el.querySelector('.summary')?.textContent).toContain('Checkout arrives with payments.');
    expect(el.querySelector<HTMLButtonElement>('.summary button')?.disabled).toBe(true);
    expect(el.querySelector('.needs-attention')).toBeNull();
  });

  it('flagged unavailable line shows remove hint and no line total', async () => {
    const { fixture, el } = setup(
      basketOf(
        line({
          issue: 'unavailable',
          productName: null,
          unitPrice: null,
          lineTotal: null,
          available: 0,
          thumbnailUrl: null,
        }),
      ),
    );
    await fixture.whenStable();
    expect(el.textContent).toContain('No longer available — remove it');
    expect(el.querySelector('.line-total')).toBeNull();
    const warning = el.querySelector<HTMLElement>('.line > .warning')!;
    expect(warning).not.toBeNull();
    expect(warning.classList.contains('empty')).toBe(false);
    expect(getComputedStyle(warning).gridArea.replace(/\s/g, '')).toBe('warn');
    expect(el.querySelector('.needs-attention')?.textContent).toContain(
      'Some items need attention.',
    );
    expect(el.querySelector('[aria-label^="Remove"]')).not.toBeNull();
  });

  it('flagged insufficient-stock line asks to lower the quantity', async () => {
    const { fixture, el } = setup(
      basketOf(line({ issue: 'insufficientStock', quantity: 4, available: 2, lineTotal: null })),
    );
    await fixture.whenStable();
    expect(el.textContent).toContain('Only 2 left — lower the quantity');
    expect(el.querySelector('.line-total')).toBeNull();
  });

  it('stepper calls setQuantity', async () => {
    const { fixture, el, fake } = setup(basketOf(line({ quantity: 2 })));
    await fixture.whenStable();
    el.querySelector<HTMLButtonElement>('[aria-label^="Increase"]')!.click();
    expect(fake.setQuantity).toHaveBeenCalledWith('v1', 3);
    await fixture.whenStable();
    el.querySelector<HTMLButtonElement>('[aria-label^="Decrease"]')!.click();
    expect(fake.setQuantity).toHaveBeenLastCalledWith('v1', 1);
  });

  it('rapid clicks send absolute targets in order', async () => {
    let release!: () => void;
    const { fixture, el, fake } = setup(basketOf(line({ quantity: 2 })));
    fake.setQuantity.mockImplementationOnce(() => new Promise<void>((r) => (release = r)));
    await fixture.whenStable();
    const plus = el.querySelector<HTMLButtonElement>('[aria-label^="Increase"]')!;
    plus.click();
    plus.click();
    expect(fake.setQuantity.mock.calls).toEqual([
      ['v1', 3],
      ['v1', 4],
    ]);
    release();
  });

  it('minus is disabled at 1 and plus at 10', async () => {
    const { fixture, el } = setup(
      basketOf(line({ quantity: 1 }), line({ variantId: 'v2', quantity: 10, available: 20 })),
    );
    await fixture.whenStable();
    const minus = el.querySelectorAll<HTMLButtonElement>('[aria-label^="Decrease"]');
    const plus = el.querySelectorAll<HTMLButtonElement>('[aria-label^="Increase"]');
    expect(minus[0].disabled).toBe(true);
    expect(minus[1].disabled).toBe(false);
    expect(plus[0].disabled).toBe(false);
    expect(plus[1].disabled).toBe(true);
  });

  it('delete removes the line', async () => {
    const { fixture, el, fake } = setup(basketOf(line()));
    await fixture.whenStable();
    el.querySelector<HTMLButtonElement>('[aria-label^="Remove"]')!.click();
    expect(fake.remove).toHaveBeenCalledWith('v1');
  });

  it('shows the server message when a change is rejected', async () => {
    const { fixture, el, fake } = setup(basketOf(line()));
    const open = vi.spyOn(TestBed.inject(MatSnackBar), 'open');
    fake.setQuantity.mockRejectedValue(
      new HttpErrorResponse({ status: 409, error: { title: 'Only 3 left.' }, statusText: 'x' }),
    );
    await fixture.whenStable();
    el.querySelector<HTMLButtonElement>('[aria-label^="Increase"]')!.click();
    await fixture.whenStable();
    expect(open).toHaveBeenCalledWith('Only 3 left.', 'Dismiss', expect.anything());
  });

  it('empty state', async () => {
    const { fixture, el } = setup(basketOf());
    await fixture.whenStable();
    expect(el.textContent).toContain('Your cart is empty');
    expect(el.querySelector('a[href="/"]')?.textContent).toContain('Start shopping');
    expect(el.querySelector('.summary')).toBeNull();
  });

  it('shows a skeleton while loading', async () => {
    const { fixture, el } = setup(null);
    await fixture.whenStable();
    expect(el.querySelector('[aria-busy="true"]')).not.toBeNull();
    expect(el.textContent).not.toContain('Your cart is empty');
  });

  it('shows an error with Retry when the first load failed', async () => {
    const { fixture, el, fake } = setup(null);
    fake.loadFailed.set(true);
    await fixture.whenStable();
    expect(el.textContent).toContain("We couldn't load your cart");
    fake.load.mockClear();
    el.querySelector<HTMLButtonElement>('.retry')!.click();
    expect(fake.load).toHaveBeenCalled();
  });
});
