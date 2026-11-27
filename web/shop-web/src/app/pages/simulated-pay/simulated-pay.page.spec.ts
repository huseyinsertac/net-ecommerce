import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { of } from 'rxjs';
import { OrderingApi } from '../../api/ordering-api.service';
import { PaymentsApi } from '../../api/payments-api.service';
import { SimulatedPayPage } from './simulated-pay.page';

function setup() {
  const payments = { simulate: vi.fn().mockReturnValue(of(undefined)) };
  const ordering = {
    order: vi.fn().mockReturnValue(of({ id: 'o1', orderNumber: 'VL-1001', total: 249.5 })),
  };
  TestBed.configureTestingModule({
    providers: [
      provideRouter([]),
      { provide: PaymentsApi, useValue: payments },
      { provide: OrderingApi, useValue: ordering },
    ],
  });
  const nav = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
  const fixture = TestBed.createComponent(SimulatedPayPage);
  fixture.componentRef.setInput('orderId', 'o1');
  const el = fixture.nativeElement as HTMLElement;
  const click = (label: string) =>
    [...el.querySelectorAll('button')].find((b) => b.textContent?.includes(label))!.click();
  return { payments, nav, fixture, el, click };
}

describe('SimulatedPayPage', () => {
  it('shows the order number and total', async () => {
    const { fixture, el } = setup();
    await fixture.whenStable();
    expect(el.textContent).toContain('Test payment');
    expect(el.textContent).toContain('This is a simulated payment page. No real money moves.');
    expect(el.textContent).toContain('VL-1001');
    expect(el.textContent).toContain('$249.50');
  });

  it('Pay sends succeed:true and navigates with checkout=success', async () => {
    const { payments, nav, fixture, click } = setup();
    await fixture.whenStable();
    click('Pay');
    await fixture.whenStable();
    expect(payments.simulate).toHaveBeenCalledWith('o1', true);
    expect(nav).toHaveBeenCalledWith(['/orders', 'o1'], { queryParams: { checkout: 'success' } });
  });

  it('Decline sends succeed:false and navigates with checkout=cancelled', async () => {
    const { payments, nav, fixture, click } = setup();
    await fixture.whenStable();
    click('Decline');
    await fixture.whenStable();
    expect(payments.simulate).toHaveBeenCalledWith('o1', false);
    expect(nav).toHaveBeenCalledWith(['/orders', 'o1'], { queryParams: { checkout: 'cancelled' } });
  });
});
