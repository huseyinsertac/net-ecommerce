import { routes } from './app.routes';

describe('routes', () => {
  it.each([
    ['', 'VoltLane'],
    ['search', 'Search · VoltLane'],
    ['**', 'Page not found · VoltLane'],
  ])('%j has the tab title %j', (path, title) => {
    expect(routes.find((r) => r.path === path)?.title).toBe(title);
  });
});

describe('product route', () => {
  it('has no static title, which the router would re-apply on every variant change', () => {
    expect(routes.find((r) => r.path === 'p/:slug')?.title).toBeUndefined();
  });
});
