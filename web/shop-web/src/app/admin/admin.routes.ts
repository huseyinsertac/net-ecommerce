import { Routes } from '@angular/router';
import { AdminShellComponent } from './admin-shell/admin-shell.component';

export const adminRoutes: Routes = [
  {
    path: '',
    component: AdminShellComponent,
    children: [
      { path: '', pathMatch: 'full', redirectTo: 'orders' },
      {
        path: 'orders',
        title: 'Orders · Admin · VoltLane',
        loadComponent: () =>
          import('./orders/admin-orders.page').then((m) => m.AdminOrdersPage),
      },
      {
        path: 'orders/:id',
        title: 'Order · Admin · VoltLane',
        loadComponent: () =>
          import('./order-detail/admin-order-detail.page').then((m) => m.AdminOrderDetailPage),
      },
    ],
  },
];
