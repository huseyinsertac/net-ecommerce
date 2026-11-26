import { Routes } from '@angular/router';
import { authGuard, guestGuard } from './core/auth/auth.guard';

export const routes: Routes = [
  {
    path: '',
    pathMatch: 'full',
    title: 'VoltLane',
    loadComponent: () => import('./pages/home/home.component').then((m) => m.HomeComponent),
  },
  {
    path: 'login',
    title: 'Sign in · VoltLane',
    canActivate: [guestGuard],
    loadComponent: () => import('./pages/login/login.page').then((m) => m.LoginPage),
  },
  {
    path: 'register',
    title: 'Create account · VoltLane',
    canActivate: [guestGuard],
    loadComponent: () => import('./pages/register/register.page').then((m) => m.RegisterPage),
  },
  {
    path: 'c/:slug',
    loadComponent: () =>
      import('./pages/product-list/product-list.page').then((m) => m.ProductListPage),
  },
  {
    path: 'search',
    title: 'Search · VoltLane',
    loadComponent: () =>
      import('./pages/product-list/product-list.page').then((m) => m.ProductListPage),
  },
  {
    path: 'p/:slug',
    // No static title: the page sets it (a static one is re-applied on every navigation end).
    loadComponent: () =>
      import('./pages/product-detail/product-detail.page').then((m) => m.ProductDetailPage),
  },
  {
    path: 'cart',
    title: 'Cart · VoltLane',
    loadComponent: () => import('./pages/cart/cart.page').then((m) => m.CartPage),
  },
  {
    path: 'checkout',
    title: 'Checkout · VoltLane',
    canActivate: [authGuard],
    loadComponent: () => import('./pages/checkout/checkout.page').then((m) => m.CheckoutPage),
  },
  {
    // Registered as its own segment path so a later `orders/:id` route cannot shadow it.
    path: 'orders/:id/progress',
    title: 'Placing your order · VoltLane',
    canActivate: [authGuard],
    loadComponent: () =>
      import('./pages/checkout-progress/checkout-progress.page').then((m) => m.CheckoutProgressPage),
  },
  {
    path: 'orders/:id',
    title: 'Order · VoltLane',
    canActivate: [authGuard],
    loadComponent: () =>
      import('./pages/order-detail/order-detail.page').then((m) => m.OrderDetailPage),
  },
  {
    path: 'orders',
    title: 'My orders · VoltLane',
    canActivate: [authGuard],
    loadComponent: () => import('./pages/orders/orders.page').then((m) => m.OrdersPage),
  },
  {
    path: 'pay/simulated/:orderId',
    title: 'Test payment · VoltLane',
    canActivate: [authGuard],
    loadComponent: () =>
      import('./pages/simulated-pay/simulated-pay.page').then((m) => m.SimulatedPayPage),
  },
  {
    path: '**',
    title: 'Page not found · VoltLane',
    loadComponent: () =>
      import('./shared/not-found/not-found.component').then((m) => m.NotFoundComponent),
  },
];
