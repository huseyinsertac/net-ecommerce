import { MoneyPipe } from './money.pipe';

describe('MoneyPipe', () => {
  it('formats USD en-US', () => {
    expect(new MoneyPipe().transform(1299)).toBe('$1,299.00');
  });
});
