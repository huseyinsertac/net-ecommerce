import { Routes } from '@angular/router';

export const routes: Routes = [
  {
    path: '',
    pathMatch: 'full',
    title: 'VoltLane',
    loadComponent: () => import('./pages/home/home.component').then((m) => m.HomeComponent),
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
    title: 'VoltLane', // replaced by the product name once it loads
    loadComponent: () =>
      import('./pages/product-detail/product-detail.page').then((m) => m.ProductDetailPage),
  },
  {
    path: '**',
    title: 'Page not found · VoltLane',
    loadComponent: () =>
      import('./shared/not-found/not-found.component').then((m) => m.NotFoundComponent),
  },
];
