import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { authInterceptor } from './auth.interceptor';
import { AuthService } from './auth.service';

const res = (token: string) => ({
  accessToken: token,
  expiresAtUtc: '2030-01-01T00:00:00Z',
  user: { id: 'u1', email: 'a@x.io', displayName: 'Ada', roles: [], isDemo: false },
});
const unauthorized = { status: 401, statusText: 'Unauthorized' };

describe('authInterceptor', () => {
  let http: HttpClient;
  let ctrl: HttpTestingController;
  let auth: AuthService;

  beforeEach(async () => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors([authInterceptor])),
        provideHttpClientTesting(),
      ],
    });
    http = TestBed.inject(HttpClient);
    ctrl = TestBed.inject(HttpTestingController);
    auth = TestBed.inject(AuthService);
    const p = auth.login('a@x.io', 'pw');
    ctrl.expectOne('/api/identity/login').flush(res('old'));
    await p;
  });

  afterEach(() => ctrl.verify());

  const tick = () => new Promise((r) => setTimeout(r));

  it('adds bearer to /api/catalog', () => {
    http.get('/api/catalog/products').subscribe();
    expect(ctrl.expectOne('/api/catalog/products').request.headers.get('Authorization')).toBe(
      'Bearer old',
    );
  });

  it('does not add bearer to /api/identity/refresh', () => {
    http.post('/api/identity/refresh', null).subscribe();
    expect(ctrl.expectOne('/api/identity/refresh').request.headers.has('Authorization')).toBe(
      false,
    );
  });

  it('adds bearer to /api/identity/me but not demo or login', () => {
    http.get('/api/identity/me').subscribe();
    http.post('/api/identity/demo/admin', null).subscribe();
    http.post('/api/identity/login', {}).subscribe();
    expect(ctrl.expectOne('/api/identity/me').request.headers.has('Authorization')).toBe(true);
    expect(ctrl.expectOne('/api/identity/demo/admin').request.headers.has('Authorization')).toBe(
      false,
    );
    expect(ctrl.expectOne('/api/identity/login').request.headers.has('Authorization')).toBe(false);
  });

  it('401 → refresh → retries once with new token', async () => {
    const got: unknown[] = [];
    http.get('/api/basket').subscribe((v) => got.push(v));
    ctrl.expectOne('/api/basket').flush({}, unauthorized);
    await tick();
    ctrl.expectOne('/api/identity/refresh').flush(res('new'));
    await tick();
    const retry = ctrl.expectOne('/api/basket');
    expect(retry.request.headers.get('Authorization')).toBe('Bearer new');
    retry.flush({ ok: 1 });
    expect(got).toEqual([{ ok: 1 }]);
  });

  it('two parallel 401s trigger one refresh', async () => {
    http.get('/api/basket').subscribe();
    http.get('/api/catalog/x').subscribe();
    ctrl.expectOne('/api/basket').flush({}, unauthorized);
    ctrl.expectOne('/api/catalog/x').flush({}, unauthorized);
    await tick();
    ctrl.expectOne('/api/identity/refresh').flush(res('new'));
    await tick();
    const retries = ctrl.match((r) => r.headers.get('Authorization') === 'Bearer new');
    expect(retries.length).toBe(2);
    retries.forEach((r) => r.flush({}));
  });

  it('a retry that 401s again errors without another refresh', async () => {
    const errors: number[] = [];
    http.get('/api/basket').subscribe({ error: (e: { status: number }) => errors.push(e.status) });
    ctrl.expectOne('/api/basket').flush({}, unauthorized);
    await tick();
    ctrl.expectOne('/api/identity/refresh').flush(res('new'));
    await tick();
    ctrl.expectOne('/api/basket').flush({}, unauthorized);
    expect(errors).toEqual([401]);
  });

  it('refresh fails → session cleared, original error rethrown', async () => {
    const errors: number[] = [];
    http.get('/api/basket').subscribe({ error: (e: { status: number }) => errors.push(e.status) });
    ctrl.expectOne('/api/basket').flush({}, unauthorized);
    await tick();
    ctrl.expectOne('/api/identity/refresh').flush({}, unauthorized);
    await tick();
    expect(errors).toEqual([401]);
    expect(auth.user()).toBeNull();
    expect(auth.accessToken()).toBeNull();
  });

  it('refresh 429 → original 401 rethrown, session kept', async () => {
    const errors: number[] = [];
    http.get('/api/basket').subscribe({ error: (e: { status: number }) => errors.push(e.status) });
    ctrl.expectOne('/api/basket').flush({}, unauthorized);
    await tick();
    ctrl
      .expectOne('/api/identity/refresh')
      .flush({ type: 'rate_limited' }, { status: 429, statusText: 'Too Many' });
    await tick();
    expect(errors).toEqual([401]);
    expect(auth.user()).not.toBeNull();
  });

  it('does not touch non-401 errors', () => {
    const errors: number[] = [];
    http.get('/api/basket').subscribe({ error: (e: { status: number }) => errors.push(e.status) });
    ctrl.expectOne('/api/basket').flush({}, { status: 404, statusText: 'NF' });
    expect(errors).toEqual([404]);
  });
});
