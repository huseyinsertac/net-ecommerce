import { stockLabel } from './stock-label';

describe('stockLabel', () => {
  it.each([
    [0, 'Out of stock'],
    [1, 'Only 1 left'],
    [5, 'Only 5 left'],
    [6, 'In stock'],
  ])('%i -> %s', (n, label) => {
    expect(stockLabel(n)).toBe(label);
  });
});
