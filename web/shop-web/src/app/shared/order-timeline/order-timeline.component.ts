import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { MatIconModule } from '@angular/material/icon';
import type { OrderDto, OrderStatus } from '../../api/ordering-api.service';

const STEPS: OrderStatus[] = ['Placed', 'Paid', 'Shipped', 'Delivered'];

const REASONS: Record<string, string> = {
  OutOfStock: 'an item sold out',
  PaymentFailed: 'payment was declined',
  PaymentExpired: "payment wasn't completed in time",
  Admin: 'cancelled by the store',
};

interface Step {
  label: string;
  at: string | null;
  state: 'reached' | 'future' | 'cancelled';
}

/** The order status timeline, shared by the customer and admin order pages. */
@Component({
  selector: 'app-order-timeline',
  imports: [DatePipe, MatIconModule],
  templateUrl: './order-timeline.component.html',
  styleUrl: './order-timeline.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class OrderTimelineComponent {
  readonly order = input.required<Pick<OrderDto, 'status' | 'cancellationReason' | 'history'>>();

  protected readonly steps = computed<Step[]>(() => {
    const order = this.order();
    const at = (s: OrderStatus) => order.history.find((h) => h.status === s)?.atUtc ?? null;
    const rank = STEPS.indexOf(order.status);
    const cancelled = order.status === 'Cancelled';
    const reached = STEPS.map<Step>((label, i) => {
      const time = at(label);
      const done = time !== null || (!cancelled && i <= rank);
      return { label, at: time, state: done ? 'reached' : 'future' };
    });
    if (!cancelled) return reached;
    const before = reached.filter((s) => s.state === 'reached');
    const reason = order.cancellationReason ? REASONS[order.cancellationReason] : null;
    return [
      ...before,
      {
        label: reason ? `Cancelled: ${reason}` : 'Cancelled',
        at: at('Cancelled'),
        state: 'cancelled',
      },
    ];
  });
}
