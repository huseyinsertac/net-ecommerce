import { TestBed } from '@angular/core/testing';
import { OrderStatusChipComponent } from './order-status-chip.component';

describe('OrderStatusChipComponent', () => {
  it.each([
    ['Placed', 'neutral'],
    ['Paid', 'primary'],
    ['Shipped', 'success'],
    ['Delivered', 'success'],
    ['Cancelled', 'warn'],
  ] as const)('renders %s with the %s tone', (status, tone) => {
    const fixture = TestBed.createComponent(OrderStatusChipComponent);
    fixture.componentRef.setInput('status', status);
    fixture.detectChanges();
    const chip = (fixture.nativeElement as HTMLElement).querySelector('.chip')!;
    expect(chip.textContent?.trim()).toBe(status);
    expect(chip.classList.contains(tone)).toBe(true);
  });
});
