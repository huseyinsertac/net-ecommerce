import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { BRAND } from '../../brand';
import { HeaderComponent } from './header.component';

@Component({ template: '' })
class Dummy {}

describe('HeaderComponent', () => {
  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [HeaderComponent],
      providers: [
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
    expect(toggle.getAttribute('aria-expanded')).toBe('true');
    toggle.click();
    await fixture.whenStable();
    expect(form.classList.contains('open')).toBe(false);
  });
});
