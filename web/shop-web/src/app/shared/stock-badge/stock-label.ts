export type StockLabel = 'Out of stock' | `Only ${number} left` | 'In stock';

export function stockLabel(available: number): StockLabel {
  if (available <= 0) return 'Out of stock';
  if (available <= 5) return `Only ${available} left`;
  return 'In stock';
}
