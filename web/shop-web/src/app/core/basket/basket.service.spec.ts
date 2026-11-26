import { HttpErrorResponse, provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Subject } from 'rxjs';
import { AuthService, CurrentUser } from '../auth/auth.service';
import { Basket, BasketService, basketErrorMessage } from './basket.service';

const line = (over: Partial<Basket['lines'][number]> = {}): Basket['lines'][number] => ({
  variantId: 'v1',
  productSlug: 'aurora-x',
  productName: 'Aurora X',
  brand: 'Voltra',
  sku: 'BLK-128',
  optionsLabel: 'Black / 128 GB',
  thumbnailUrl: '/images/products/a-400.webp',
  unitPrice: 100,
  quantity: 2,
  available: 5,
  lineTotal: 200,
  issue: null,
  ...over,
});

const basketOf = (...lines: Basket['lines']): Basket => ({
  lines,
  itemCount: lines.reduce((n, l) => n + l.quantity, 0),
  subtotal: lines.reduce((n, l) => n + (l.lineTotal ?? 0), 0),
  hasIssues: lines.some((l) => l.issue),
});

const user: CurrentUser = { id: 'u1', email: 'a@x.io', displayName: 'Ada', roles: [], isDemo: false };
const macrotask = () => new Promise<void>((r) => setTimeout(r));

describe('BasketService', () => {
  let http: HttpTestingController;
  let svc: BasketService;
  const currentUser = signal<CurrentUser | null>(null);
  const signedIn = new Subject<CurrentUser>();

  beforeEach(() => {
    currentUser.set(null);
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: AuthService, useValue: { user: currentUser, signedIn: signedIn.asObservable() } },
      ],
    });
    http = TestBed.inject(HttpTestingController);
    svc = TestBed.inject(BasketService);
  });

  afterEach(() => http.verify());

  it('count reflects itemCount, and is 0 before anything loads', async () => {
    expect(svc.count()).toBe(0);
    const p = svc.load();
    await macrotask();
    http.expectOne('/api/basket').flush(basketOf(line({ quantity: 3, lineTotal: 300 })));
    await p;
    expect(svc.count()).toBe(3);
    expect(svc.basket()?.lines.length).toBe(1);
  });

  it('mergeAfterSignIn posts merge then sets basket', async () => {
    const p = svc.mergeAfterSignIn();
    await macrotask();
    const req = http.expectOne('/api/basket/merge');
    expect(req.request.method).toBe('POST');
    req.flush(basketOf(line({ quantity: 4, lineTotal: 400 })));
    await p;
    expect(svc.count()).toBe(4);
  });

  it('merges when the auth service reports a sign-in', async () => {
    signedIn.next(user);
    await macrotask();
    http.expectOne('/api/basket/merge').flush(basketOf(line()));
    await macrotask();
    expect(svc.count()).toBe(2);
  });

  it('sign-out reloads the anonymous basket', async () => {
    currentUser.set(user);
    TestBed.tick();
    http.expectNone('/api/basket');
    currentUser.set(null);
    TestBed.tick();
    await macrotask();
    http.expectOne('/api/basket').flush(basketOf());
    await macrotask();
    expect(svc.count()).toBe(0);
    expect(svc.basket()?.lines).toEqual([]);
  });

  it('serializes quantity changes: one request in flight at a time, in order', async () => {
    const a = svc.setQuantity('v1', 1);
    const b = svc.setQuantity('v1', 2);
    await macrotask();
    const first = http.expectOne('/api/basket/items/v1');
    expect(first.request.method).toBe('PUT');
    expect(first.request.body).toEqual({ quantity: 1 });
    first.flush(basketOf(line({ quantity: 1, lineTotal: 100 })));
    await a;
    await macrotask();
    const second = http.expectOne('/api/basket/items/v1');
    expect(second.request.body).toEqual({ quantity: 2 });
    second.flush(basketOf(line({ quantity: 2, lineTotal: 200 })));
    await b;
    expect(svc.count()).toBe(2);
  });

  it('a failed change rejects, keeps the basket and does not block the queue', async () => {
    const ok = svc.load();
    await macrotask();
    http.expectOne('/api/basket').flush(basketOf(line()));
    await ok;

    const bad = svc.setQuantity('v1', 9);
    const next = svc.remove('v1');
    const rejected = bad.then(
      () => null,
      (e: unknown) => e,
    );
    await macrotask();
    http
      .expectOne('/api/basket/items/v1')
      .flush(
        { type: 'basket.insufficient_stock', title: 'Only 5 left.', status: 409 },
        { status: 409, statusText: 'Conflict' },
      );
    const err = await rejected;
    expect(err).toBeInstanceOf(HttpErrorResponse);
    expect(basketErrorMessage(err)).toBe('Only 5 left.');
    expect(svc.count()).toBe(2);

    await macrotask();
    const del = http.expectOne('/api/basket/items/v1');
    expect(del.request.method).toBe('DELETE');
    del.flush(basketOf());
    await next;
    expect(svc.count()).toBe(0);
  });

  it('a failed merge reloads the basket', async () => {
    signedIn.next(user);
    await macrotask();
    http.expectOne('/api/basket/merge').flush({}, { status: 500, statusText: 'x' });
    await macrotask();
    await macrotask();
    http.expectOne('/api/basket').flush(basketOf());
  });

  describe('add', () => {
    it('reads the quantity when it runs, after a queued merge lands', async () => {
      const merge = svc.mergeAfterSignIn();
      const added = svc.add('v1', 1);
      await macrotask();
      http.expectOne('/api/basket/merge').flush(basketOf(line({ quantity: 4, lineTotal: 400 })));
      await merge;
      await macrotask();
      const put = http.expectOne('/api/basket/items/v1');
      expect(put.request.body).toEqual({ quantity: 5 });
      put.flush(basketOf(line({ quantity: 5, lineTotal: 500 })));
      expect(await added).toBe(true);
    });

    it('loads first when the basket is unknown, so it cannot lower a server quantity', async () => {
      const added = svc.add('v1', 1);
      await macrotask();
      http.expectOne('/api/basket').flush(basketOf(line({ quantity: 6, lineTotal: 600 })));
      await macrotask();
      const put = http.expectOne('/api/basket/items/v1');
      expect(put.request.body).toEqual({ quantity: 7 });
      put.flush(basketOf(line({ quantity: 7, lineTotal: 700 })));
      await added;
    });

    it('reloads after a failed load before adding', async () => {
      const failed = svc.load();
      await macrotask();
      http.expectOne('/api/basket').flush({}, { status: 500, statusText: 'x' });
      await failed;
      const added = svc.add('v1', 1);
      await macrotask();
      http.expectOne('/api/basket').flush(basketOf(line({ quantity: 3, lineTotal: 300 })));
      await macrotask();
      const put = http.expectOne('/api/basket/items/v1');
      expect(put.request.body).toEqual({ quantity: 4 });
      put.flush(basketOf(line({ quantity: 4, lineTotal: 400 })));
      await added;
    });

    it('sends nothing and resolves false at the maximum', async () => {
      const ok = svc.load();
      await macrotask();
      http.expectOne('/api/basket').flush(basketOf(line({ quantity: 10, lineTotal: 1000 })));
      await ok;
      const added = svc.add('v1', 1);
      await macrotask();
      http.expectNone('/api/basket/items/v1');
      expect(await added).toBe(false);
    });
  });

  it('clear empties the basket', async () => {
    const p = svc.clear();
    await macrotask();
    const req = http.expectOne('/api/basket');
    expect(req.request.method).toBe('DELETE');
    req.flush(null, { status: 204, statusText: 'No Content' });
    await p;
    expect(svc.count()).toBe(0);
    expect(svc.basket()?.lines).toEqual([]);
  });

  it('a failed load flags the error and a later load clears it', async () => {
    const p = svc.load();
    await macrotask();
    http.expectOne('/api/basket').flush({}, { status: 500, statusText: 'x' });
    await p;
    expect(svc.loadFailed()).toBe(true);
    const q = svc.load();
    await macrotask();
    http.expectOne('/api/basket').flush(basketOf());
    await q;
    expect(svc.loadFailed()).toBe(false);
  });
});

describe('basketErrorMessage', () => {
  const http = (status: number, body: unknown) =>
    new HttpErrorResponse({ status, error: body, statusText: 'x' });

  it('uses the problem title for 409 and 404', () => {
    expect(basketErrorMessage(http(409, { title: 'Out of stock.' }))).toBe('Out of stock.');
    expect(basketErrorMessage(http(404, { title: 'This item is no longer available.' }))).toBe(
      'This item is no longer available.',
    );
  });

  it('falls back for a 4xx without a title, and is silent for server and network errors', () => {
    expect(basketErrorMessage(http(400, null))).toBe("We couldn't update your cart.");
    expect(basketErrorMessage(http(500, null))).toBeNull();
    expect(basketErrorMessage(http(0, null))).toBeNull();
  });
});
