import { Routes } from '@angular/router';

export const routes: Routes = [
  {
    path: '',
    pathMatch: 'full',
    loadComponent: () => import('./pages/home/home.component').then((m) => m.HomeComponent),
  },
  {
    path: 'c/:slug',
    loadComponent: () =>
      import('./pages/product-list/product-list.page').then((m) => m.ProductListPage),
  },
  {
    path: 'search',
    loadComponent: () =>
      import('./pages/product-list/product-list.page').then((m) => m.ProductListPage),
  },
  {
    path: 'p/:slug',
    loadComponent: () =>
      import('./pages/product-detail/product-detail.page').then((m) => m.ProductDetailPage),
  },
  {
    path: '**',
    loadComponent: () =>
      import('./shared/not-found/not-found.component').then((m) => m.NotFoundComponent),
  },
];
