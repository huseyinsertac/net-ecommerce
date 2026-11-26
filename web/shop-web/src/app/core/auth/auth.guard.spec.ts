import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { ActivatedRouteSnapshot, Router, convertToParamMap, RouterStateSnapshot, UrlTree } from '@angular/router';
import { Observable, firstValueFrom } from 'rxjs';
import { authGuard, guestGuard } from './auth.guard';
import { AuthService } from './auth.service';

describe('authGuard', () => {
  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
  });

  it('redirects to /login with returnUrl', async () => {
    const auth = TestBed.inject(AuthService);
    const http = TestBed.inject(HttpTestingController);
    const result = firstValueFrom(
      TestBed.runInInjectionContext(() =>
        authGuard({} as ActivatedRouteSnapshot, { url: '/basket?x=1' } as RouterStateSnapshot),
      ) as Observable<boolean | UrlTree>,
    );
    const init = auth.initialize();
    http.expectOne('/api/identity/refresh').flush({}, { status: 401, statusText: 'Unauthorized' });
    await init;
    TestBed.tick();
    const tree = (await result) as UrlTree;
    expect(TestBed.inject(Router).serializeUrl(tree)).toBe('/login?returnUrl=%2Fbasket%3Fx%3D1');
  });

  describe('guestGuard', () => {
    async function run(returnUrl?: string) {
      const auth = TestBed.inject(AuthService);
      const http = TestBed.inject(HttpTestingController);
      const route = {
        queryParamMap: convertToParamMap(returnUrl ? { returnUrl } : {}),
      } as ActivatedRouteSnapshot;
      const result = firstValueFrom(
        TestBed.runInInjectionContext(() =>
          guestGuard(route, {} as RouterStateSnapshot),
        ) as Observable<boolean | UrlTree>,
      );
      const init = auth.initialize();
      http.expectOne('/api/identity/refresh').flush({
        accessToken: 't',
        expiresAtUtc: '2030-01-01T00:00:00Z',
        user: { id: 'u', email: 'a@x.io', displayName: 'Ada', roles: [], isDemo: false },
      });
      await init;
      TestBed.tick();
      return TestBed.inject(Router).serializeUrl((await result) as UrlTree);
    }

    it('redirects a signed-in visitor to the returnUrl', async () => {
      expect(await run('/basket')).toBe('/basket');
    });

    it('falls back to / for an off-site returnUrl', async () => {
      expect(await run('//evil.example')).toBe('/');
    });

    it('lets a signed-out visitor through', async () => {
      const auth = TestBed.inject(AuthService);
      const http = TestBed.inject(HttpTestingController);
      const result = firstValueFrom(
        TestBed.runInInjectionContext(() =>
          guestGuard(
            { queryParamMap: convertToParamMap({}) } as ActivatedRouteSnapshot,
            {} as RouterStateSnapshot,
          ),
        ) as Observable<boolean | UrlTree>,
      );
      const init = auth.initialize();
      http.expectOne('/api/identity/refresh').flush({}, { status: 401, statusText: 'Unauthorized' });
      await init;
      TestBed.tick();
      expect(await result).toBe(true);
    });
  });
});
