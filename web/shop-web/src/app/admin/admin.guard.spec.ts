import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { ActivatedRouteSnapshot, Router, RouterStateSnapshot, UrlTree } from '@angular/router';
import { Observable, firstValueFrom } from 'rxjs';
import { AuthService } from '../core/auth/auth.service';
import { adminGuard } from './admin.guard';

describe('adminGuard', () => {
  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
  });

  async function run(refresh: 'anonymous' | 'customer' | 'admin') {
    const auth = TestBed.inject(AuthService);
    const http = TestBed.inject(HttpTestingController);
    const result = firstValueFrom(
      TestBed.runInInjectionContext(() =>
        adminGuard({} as ActivatedRouteSnapshot, { url: '/admin/orders?page=2' } as RouterStateSnapshot),
      ) as Observable<boolean | UrlTree>,
    );
    const init = auth.initialize();
    const req = http.expectOne('/api/identity/refresh');
    if (refresh === 'anonymous') {
      req.flush({}, { status: 401, statusText: 'Unauthorized' });
    } else {
      req.flush({
        accessToken: 't',
        expiresAtUtc: '2030-01-01T00:00:00Z',
        user: {
          id: 'u',
          email: 'a@x.io',
          displayName: 'Ada',
          roles: refresh === 'admin' ? ['Admin'] : [],
          isDemo: false,
        },
      });
    }
    await init;
    TestBed.tick();
    return result;
  }

  it('sends a signed-out visitor to /login with the returnUrl', async () => {
    const tree = (await run('anonymous')) as UrlTree;
    expect(TestBed.inject(Router).serializeUrl(tree)).toBe(
      '/login?returnUrl=%2Fadmin%2Forders%3Fpage%3D2',
    );
  });

  it('sends a signed-in non-admin to /', async () => {
    const tree = (await run('customer')) as UrlTree;
    expect(TestBed.inject(Router).serializeUrl(tree)).toBe('/');
  });

  it('lets an admin through', async () => {
    expect(await run('admin')).toBe(true);
  });
});
