import { HttpErrorResponse } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { AuthService } from '../../core/auth/auth.service';
import { RegisterPage } from './register.page';

describe('RegisterPage', () => {
  const auth = { register: vi.fn() };

  beforeEach(() => {
    auth.register.mockReset();
    TestBed.configureTestingModule({
      imports: [RegisterPage],
      providers: [provideRouter([]), { provide: AuthService, useValue: auth }],
    });
  });

  async function submit() {
    const f = TestBed.createComponent(RegisterPage);
    await f.whenStable();
    const el = f.nativeElement as HTMLElement;
    const set = (sel: string, v: string) => {
      const i = el.querySelector<HTMLInputElement>(sel)!;
      i.value = v;
      i.dispatchEvent(new Event('input'));
    };
    set('input[autocomplete="name"]', 'Ada');
    set('input[type="email"]', 'a@x.io');
    set('input[type="password"]', 'secret123');
    el.querySelector('form')!.dispatchEvent(new Event('submit'));
    await f.whenStable();
    return el;
  }

  it('shows the password hint', async () => {
    const f = TestBed.createComponent(RegisterPage);
    await f.whenStable();
    expect((f.nativeElement as HTMLElement).textContent).toContain(
      'At least 8 characters, a letter and a digit.',
    );
  });

  it('registers and navigates home', async () => {
    auth.register.mockResolvedValue(undefined);
    const nav = vi.spyOn(TestBed.inject(Router), 'navigateByUrl').mockResolvedValue(true);
    await submit();
    expect(auth.register).toHaveBeenCalledWith('a@x.io', 'secret123', 'Ada');
    expect(nav).toHaveBeenCalledWith('/');
  });

  it('shows email_taken on the email field', async () => {
    auth.register.mockRejectedValue(
      new HttpErrorResponse({ status: 409, error: { type: 'identity.email_taken' } }),
    );
    const el = await submit();
    expect(el.textContent).toContain('already exists');
  });
});
