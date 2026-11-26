import { HttpErrorResponse } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { AuthService } from '../../core/auth/auth.service';
import { LoginPage } from './login.page';

describe('LoginPage', () => {
  const auth = { login: vi.fn() };

  beforeEach(() => {
    auth.login.mockReset();
    TestBed.configureTestingModule({
      imports: [LoginPage],
      providers: [provideRouter([]), { provide: AuthService, useValue: auth }],
    });
  });

  async function submit(returnUrl?: string) {
    const f = TestBed.createComponent(LoginPage);
    if (returnUrl) f.componentRef.setInput('returnUrl', returnUrl);
    await f.whenStable();
    const el = f.nativeElement as HTMLElement;
    const set = (sel: string, v: string) => {
      const i = el.querySelector<HTMLInputElement>(sel)!;
      i.value = v;
      i.dispatchEvent(new Event('input'));
    };
    set('input[type="email"]', 'a@x.io');
    set('input[type="password"]', 'secret123');
    el.querySelector('form')!.dispatchEvent(new Event('submit'));
    await f.whenStable();
    return el;
  }

  it('signs in and redirects to returnUrl', async () => {
    auth.login.mockResolvedValue(undefined);
    const nav = vi.spyOn(TestBed.inject(Router), 'navigateByUrl').mockResolvedValue(true);
    await submit('/basket');
    expect(auth.login).toHaveBeenCalledWith('a@x.io', 'secret123');
    expect(nav).toHaveBeenCalledWith('/basket');
  });

  it('ignores an off-site returnUrl', async () => {
    auth.login.mockResolvedValue(undefined);
    const nav = vi.spyOn(TestBed.inject(Router), 'navigateByUrl').mockResolvedValue(true);
    await submit('//evil.example');
    expect(nav).toHaveBeenCalledWith('/');
  });

  it('shows one inline error for 401 and a link to the demo panel', async () => {
    auth.login.mockRejectedValue(new HttpErrorResponse({ status: 401 }));
    const el = await submit();
    const alert = el.querySelector('[aria-live]')!;
    expect(alert.textContent).toContain('Email or password is incorrect.');
    expect(el.querySelector('a[href="/"]')?.textContent).toContain('demo');
  });
});
