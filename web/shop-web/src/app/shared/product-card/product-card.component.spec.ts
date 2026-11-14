import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { ProductSummary } from '../../api/catalog-api.service';
import { ProductCardComponent } from './product-card.component';

const base: ProductSummary = {
  id: '1',
  slug: 'volt-phone',
  name: 'Volt Phone',
  brand: 'Voltra',
  categorySlug: 'phones',
  imageUrl: '/images/products/volt-phone-400.webp',
  priceFrom: 699,
  priceTo: 699,
  inStock: true,
  isFeatured: false,
};

async function render(p: ProductSummary): Promise<HTMLElement> {
  TestBed.configureTestingModule({ providers: [provideRouter([])] });
  const f = TestBed.createComponent(ProductCardComponent);
  f.componentRef.setInput('product', p);
  await f.whenStable();
  return f.nativeElement as HTMLElement;
}

describe('ProductCardComponent', () => {
  it('shows no From for a single price', async () => {
    const el = await render(base);
    expect(el.textContent).not.toContain('From');
    expect(el.textContent).toContain('$699.00');
  });

  it('shows From for a price range', async () => {
    const el = await render({ ...base, priceTo: 899 });
    expect(el.textContent).toContain('From $699.00');
  });

  it('links to the product with a sized lazy thumbnail', async () => {
    const el = await render(base);
    expect(el.querySelector('a')?.getAttribute('href')).toBe('/p/volt-phone');
    const img = el.querySelector('img')!;
    expect(img.getAttribute('loading')).toBe('lazy');
    expect(img.getAttribute('width')).toBe('400');
    expect(img.getAttribute('height')).toBe('400');
    expect(el.textContent).toContain('Voltra');
    expect(el.textContent).toContain('In stock');
  });

  it('shows Out of stock', async () => {
    const el = await render({ ...base, inStock: false });
    expect(el.textContent).toContain('Out of stock');
  });
});
