import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { BRAND } from '../../brand';
import { Subject } from 'rxjs';
import { AuthService, CurrentUser } from '../../core/auth/auth.service';
import { BasketService } from '../../core/basket/basket.service';
import { HeaderComponent } from './header.component';

@Component({ template: '' })
class Dummy {}

describe('HeaderComponent', () => {
  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [HeaderComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([
          { path: 'search', component: Dummy },
          { path: '', component: Dummy },
          { path: 'c/:slug', component: Dummy },
        ]),
      ],
    });
  });

  it('header shows brand name from BRAND', async () => {
    const fixture = TestBed.createComponent(HeaderComponent);
    await fixture.whenStable();
    expect((fixture.nativeElement as HTMLElement).textContent).toContain(BRAND.name);
  });

  it('shows no cart badge while the count is 0', async () => {
    const fixture = TestBed.createComponent(HeaderComponent);
    await fixture.whenStable();
    const el = fixture.nativeElement as HTMLElement;
    expect(el.querySelector('.mat-badge')).not.toBeNull();
    expect(el.querySelector('.mat-badge-hidden')).not.toBeNull();
  });

  it('the cart link goes to /cart and the badge shows the live count', async () => {
    const count = signal(0);
    TestBed.overrideProvider(BasketService, { useValue: { count } });
    const fixture = TestBed.createComponent(HeaderComponent);
    await fixture.whenStable();
    const el = fixture.nativeElement as HTMLElement;
    const link = el.querySelector<HTMLAnchorElement>('a[href="/cart"]')!;
    expect(link).not.toBeNull();
    expect(el.querySelector('.mat-badge-hidden')).not.toBeNull();
    count.set(3);
    await fixture.whenStable();
    expect(el.querySelector('.mat-badge-hidden')).toBeNull();
    expect(el.querySelector('.mat-badge-content')?.textContent?.trim()).toBe('3');
    expect(link.getAttribute('aria-label')).toBe('Cart, 3 items');
  });

  async function submit(value: string) {
    const router = TestBed.inject(Router);
    const nav = vi.spyOn(router, 'navigate').mockResolvedValue(true);
    const fixture = TestBed.createComponent(HeaderComponent);
    await fixture.whenStable();
    const el = fixture.nativeElement as HTMLElement;
    const input = el.querySelector('input')!;
    input.value = value;
    input.dispatchEvent(new Event('input'));
    el.querySelector('form')!.dispatchEvent(new Event('submit'));
    await fixture.whenStable();
    return nav;
  }

  it('navigates to /search?q=<trimmed> on submit', async () => {
    const nav = await submit('  volt phone ');
    expect(nav).toHaveBeenCalledWith(['/search'], { queryParams: { q: 'volt phone' } });
  });

  it('ignores whitespace-only input', async () => {
    const nav = await submit('   ');
    expect(nav).not.toHaveBeenCalled();
  });

  describe('reflecting q', () => {
    async function setup(url: string) {
      const router = TestBed.inject(Router);
      await router.navigateByUrl(url);
      const fixture = TestBed.createComponent(HeaderComponent);
      await fixture.whenStable();
      const input = (fixture.nativeElement as HTMLElement).querySelector('input')!;
      const go = async (u: string) => {
        await router.navigateByUrl(u);
        await fixture.whenStable();
      };
      return { input, go };
    }

    it('shows q on /search?q=x', async () => {
      const { input } = await setup('/search?q=x');
      expect(input.value).toBe('x');
    });

    it('updates when navigating between searches', async () => {
      const { input, go } = await setup('/search?q=a');
      expect(input.value).toBe('a');
      await go('/search?q=b');
      expect(input.value).toBe('b');
    });

    it('clears when leaving /search', async () => {
      const { input, go } = await setup('/search?q=a');
      await go('/c/phones');
      expect(input.value).toBe('');
    });
  });

  it('toggles the mobile search row', async () => {
    const fixture = TestBed.createComponent(HeaderComponent);
    await fixture.whenStable();
    const el = fixture.nativeElement as HTMLElement;
    const form = el.querySelector('form')!;
    const toggle = el.querySelector<HTMLButtonElement>('.search-toggle')!;
    expect(form.classList.contains('open')).toBe(false);
    toggle.click();
    await fixture.whenStable();
    expect(form.classList.contains('open')).toBe(true);
    expect(document.activeElement).toBe(el.querySelector('input'));
    expect(toggle.getAttribute('aria-label')).toBe('Close search');
    expect(toggle.getAttribute('aria-expanded')).toBe('true');
    toggle.click();
    await fixture.whenStable();
    expect(form.classList.contains('open')).toBe(false);
  });

  describe('account area', () => {
    const user = signal<CurrentUser | null>(null);

    beforeEach(() => {
      user.set(null);
      TestBed.overrideProvider(AuthService, {
        useValue: { user, isAdmin: () => false, logout: vi.fn(), signedIn: new Subject() },
      });
    });

    it('shows Sign in when signed out, display name initial when signed in', async () => {
      const fixture = TestBed.createComponent(HeaderComponent);
      await fixture.whenStable();
      const el = fixture.nativeElement as HTMLElement;
      expect(el.querySelector('a[href="/login"]')?.textContent).toContain('Sign in');
      expect(el.querySelector('.account-button')).toBeNull();

      user.set({ id: '1', email: 'a@x.io', displayName: 'ada', roles: [], isDemo: false });
      await fixture.whenStable();
      expect(el.querySelector('a[href="/login"]')).toBeNull();
      const avatar = el.querySelector<HTMLButtonElement>('.account-button')!;
      expect(avatar.textContent?.trim()).toBe('A');
      expect(avatar.getAttribute('aria-label')).toContain('ada');
    });
  });
});
