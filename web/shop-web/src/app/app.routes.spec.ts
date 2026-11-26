import { guestGuard } from './core/auth/auth.guard';
import { routes } from './app.routes';

describe('routes', () => {
  it.each([
    ['', 'VoltLane'],
    ['search', 'Search · VoltLane'],
    ['login', 'Sign in · VoltLane'],
    ['register', 'Create account · VoltLane'],
    ['cart', 'Cart · VoltLane'],
    ['**', 'Page not found · VoltLane'],
  ])('%j has the tab title %j', (path, title) => {
    expect(routes.find((r) => r.path === path)?.title).toBe(title);
  });
});

describe('guest routes', () => {
  it.each(['login', 'register'])('%s is guarded by guestGuard', (path) => {
    expect(routes.find((r) => r.path === path)?.canActivate).toEqual([guestGuard]);
  });
});

describe('product route', () => {
  it('has no static title, which the router would re-apply on every variant change', () => {
    expect(routes.find((r) => r.path === 'p/:slug')?.title).toBeUndefined();
  });
});
