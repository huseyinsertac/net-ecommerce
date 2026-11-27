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
      {
        path: 'products',
        title: 'Products · Admin · VoltLane',
        loadComponent: () =>
          import('./products/admin-products.page').then((m) => m.AdminProductsPage),
      },
      // 'new' must come before ':id'.
      {
        path: 'products/new',
        title: 'New product · Admin · VoltLane',
        loadComponent: () =>
          import('./product-create/admin-product-create.page').then((m) => m.AdminProductCreatePage),
      },
      {
        path: 'products/:id',
        title: 'Product · Admin · VoltLane',
        loadComponent: () =>
          import('./product-edit/admin-product-edit.page').then((m) => m.AdminProductEditPage),
      },
    ],
  },
];
