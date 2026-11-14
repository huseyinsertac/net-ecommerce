import { TestBed } from '@angular/core/testing';
import { StockBadgeComponent } from './stock-badge.component';

async function render(inputs: { available?: number; inStock?: boolean }): Promise<string> {
  const f = TestBed.createComponent(StockBadgeComponent);
  for (const [k, v] of Object.entries(inputs)) f.componentRef.setInput(k, v);
  await f.whenStable();
  return ((f.nativeElement as HTMLElement).textContent ?? '').trim();
}

describe('StockBadgeComponent', () => {
  it.each([
    [0, 'Out of stock'],
    [3, 'Only 3 left'],
    [10, 'In stock'],
  ])('available=%i shows %s', async (available, text) => {
    expect(await render({ available })).toBe(text);
  });

  it('uses the inStock flag when available is not given', async () => {
    expect(await render({ inStock: true })).toBe('In stock');
    TestBed.resetTestingModule();
    expect(await render({ inStock: false })).toBe('Out of stock');
  });

  it('low stock gets a warning class; in stock and out of stock do not', async () => {
    const classes = async (available: number) => {
      TestBed.resetTestingModule();
      const f = TestBed.createComponent(StockBadgeComponent);
      f.componentRef.setInput('available', available);
      await f.whenStable();
      return (f.nativeElement as HTMLElement).querySelector('.badge')!.className;
    };
    expect(await classes(3)).toContain('low');
    expect(await classes(10)).not.toContain('low');
    expect(await classes(0)).not.toContain('low');
  });

  it('renders nothing when neither input is set', async () => {
    expect(await render({})).toBe('');
  });

  it('available wins over inStock', async () => {
    expect(await render({ available: 0, inStock: true })).toBe('Out of stock');
  });
});
