import { ProductDetail, Variant } from '../../api/catalog-api.service';
import {
  bestSelectionFor,
  initialSelection,
  isValueSelectable,
  resolveVariant,
} from './variant-logic';

function v(sku: string, color: string, size: string, available: number): Variant {
  return { id: sku, sku, options: { color, size }, price: 10, available };
}

// Red/L does not exist.
const variants = [
  v('R-S', 'red', 'S', 0),
  v('R-M', 'red', 'M', 2),
  v('B-S', 'blue', 'S', 9),
  v('B-M', 'blue', 'M', 9),
  v('B-L', 'blue', 'L', 9),
];

describe('variant-logic', () => {
  it('initialSelection skips out-of-stock first variant', () => {
    expect(initialSelection({ variants } as ProductDetail)).toEqual({ color: 'red', size: 'M' });
  });

  it('initialSelection uses the first variant when nothing is in stock', () => {
    const none = variants.map((x) => ({ ...x, available: 0 }));
    expect(initialSelection({ variants: none } as ProductDetail)).toEqual({ color: 'red', size: 'S' });
  });

  it('resolveVariant exact match', () => {
    expect(resolveVariant(variants, { color: 'blue', size: 'L' })?.sku).toBe('B-L');
    expect(resolveVariant(variants, { color: 'red', size: 'L' })).toBeUndefined();
  });

  it('isValueSelectable false when combination missing', () => {
    expect(isValueSelectable(variants, { color: 'blue', size: 'M' }, 'size', 'L')).toBe(true);
    expect(isValueSelectable(variants, { color: 'red', size: 'M' }, 'size', 'L')).toBe(false);
    expect(isValueSelectable(variants, { color: 'red', size: 'M' }, 'color', 'blue')).toBe(true);
  });

  it('bestSelectionFor falls back to existing combination', () => {
    expect(bestSelectionFor(variants, { color: 'blue', size: 'L' }, 'color', 'red')).toEqual({
      color: 'red',
      size: 'M',
    });
    expect(bestSelectionFor(variants, { color: 'red', size: 'M' }, 'size', 'S')).toEqual({
      color: 'red',
      size: 'S',
    });
  });
});
