import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { AuthService } from './auth.service';

const res = (token: string, id = 'u1', roles: string[] = ['Customer']) => ({
  accessToken: token,
  expiresAtUtc: '2030-01-01T00:00:00Z',
  user: { id, email: `${id}@x.io`, displayName: 'Ada', roles, isDemo: false },
});

describe('AuthService', () => {
  let auth: AuthService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    auth = TestBed.inject(AuthService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  async function loginAs(token = 't1', id = 'u1', roles?: string[]) {
    const p = auth.login('a@x.io', 'pw');
    http.expectOne('/api/identity/login').flush(res(token, id, roles));
    await p;
  }

  it('login sets user and token', async () => {
    const p = auth.login('a@x.io', 'pw');
    const req = http.expectOne('/api/identity/login');
    expect(req.request.body).toEqual({ email: 'a@x.io', password: 'pw' });
    req.flush(res('t1'));
    await p;
    expect(auth.user()?.displayName).toBe('Ada');
    expect(auth.accessToken()).toBe('t1');
    expect(auth.isAdmin()).toBe(false);
  });

  it('isAdmin is true for the Admin role', async () => {
    await loginAs('t1', 'u1', ['Admin']);
    expect(auth.isAdmin()).toBe(true);
  });

  it('logout clears user even if request fails', async () => {
    await loginAs();
    const p = auth.logout();
    http.expectOne('/api/identity/logout').flush(null, { status: 409, statusText: 'Conflict' });
    await p;
    expect(auth.user()).toBeNull();
    expect(auth.accessToken()).toBeNull();
  });

  it('signedIn emits on sign-in but not on a token-only refresh of the same user', async () => {
    const seen: string[] = [];
    auth.signedIn.subscribe((u) => seen.push(u.id));
    await loginAs('t1', 'u1');
    const p = auth.refresh();
    http.expectOne('/api/identity/refresh').flush(res('t2', 'u1'));
    await p;
    expect(auth.accessToken()).toBe('t2');
    expect(seen).toEqual(['u1']);
  });

  it('refresh is single-flight', async () => {
    const a = auth.refresh();
    const b = auth.refresh();
    http.expectOne('/api/identity/refresh').flush(res('t9'));
    expect(await Promise.all([a, b])).toEqual([true, true]);
  });

  it('refresh 401 clears the session; 429 keeps it', async () => {
    await loginAs();
    let p = auth.refresh();
    http
      .expectOne('/api/identity/refresh')
      .flush({ type: 'rate_limited' }, { status: 429, statusText: 'Too Many' });
    expect(await p).toBe(false);
    expect(auth.user()).not.toBeNull();
    p = auth.refresh();
    http.expectOne('/api/identity/refresh').flush({}, { status: 401, statusText: 'Unauthorized' });
    expect(await p).toBe(false);
    expect(auth.user()).toBeNull();
  });

  it('refresh 204 (no cookie) means no session: resolves false, stays signed out, no retry', async () => {
    const seen: string[] = [];
    auth.signedIn.subscribe((u) => seen.push(u.id));
    const p = auth.refresh();
    http.expectOne('/api/identity/refresh').flush(null, { status: 204, statusText: 'No Content' });
    expect(await p).toBe(false);
    expect(auth.user()).toBeNull();
    expect(auth.accessToken()).toBeNull();
    expect(seen).toEqual([]);
    http.expectNone('/api/identity/refresh');
  });

  it('refresh 204 for a signed-in user ends the local session', async () => {
    await loginAs();
    const p = auth.refresh();
    http.expectOne('/api/identity/refresh').flush(null, { status: 204, statusText: 'No Content' });
    expect(await p).toBe(false);
    expect(auth.user()).toBeNull();
  });

  it('initialize with a 204 refresh becomes ready, signed out', async () => {
    const p = auth.initialize();
    http.expectOne('/api/identity/refresh').flush(null, { status: 204, statusText: 'No Content' });
    await p;
    expect(auth.ready()).toBe(true);
    expect(auth.user()).toBeNull();
  });

  it('initialize resolves after 5s when the refresh never responds', async () => {
    vi.useFakeTimers();
    try {
      const p = auth.initialize();
      http.expectOne('/api/identity/refresh');
      expect(auth.ready()).toBe(false);
      await vi.advanceTimersByTimeAsync(5000);
      await p;
      expect(auth.ready()).toBe(true);
      expect(auth.user()).toBeNull();
    } finally {
      vi.useRealTimers();
    }
  });

  it('initialize becomes ready even when the refresh fails', async () => {
    const p = auth.initialize();
    expect(auth.ready()).toBe(false);
    http.expectOne('/api/identity/refresh').flush({}, { status: 401, statusText: 'Unauthorized' });
    await p;
    expect(auth.ready()).toBe(true);
    expect(auth.user()).toBeNull();
  });

  it('a late refresh success after logout does not sign the user back in', async () => {
    await loginAs('t1', 'u1');
    const refresh = auth.refresh();
    const pending = http.expectOne('/api/identity/refresh');
    const out = auth.logout();
    http.expectOne('/api/identity/logout').flush(null, { status: 204, statusText: 'No Content' });
    await out;
    pending.flush(res('t-late', 'u1'));
    await refresh;
    expect(auth.user()).toBeNull();
    expect(auth.accessToken()).toBeNull();
  });

  it('a late refresh 401 after a new login does not sign the user out', async () => {
    const refresh = auth.refresh();
    const pending = http.expectOne('/api/identity/refresh');
    await loginAs('t-new', 'u2');
    pending.flush({}, { status: 401, statusText: 'Unauthorized' });
    await refresh;
    expect(auth.user()?.id).toBe('u2');
    expect(auth.accessToken()).toBe('t-new');
  });

  it('a failed login does not discard an in-flight refresh', async () => {
    const refresh = auth.refresh();
    const pending = http.expectOne('/api/identity/refresh');
    const failed = auth.login('a@x.io', 'bad').catch(() => 'failed');
    http.expectOne('/api/identity/login').flush({}, { status: 401, statusText: 'Unauthorized' });
    expect(await failed).toBe('failed');
    pending.flush(res('t-restored', 'u1'));
    expect(await refresh).toBe(true);
    expect(auth.user()?.id).toBe('u1');
  });
});
