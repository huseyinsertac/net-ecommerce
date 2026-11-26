import { HttpErrorResponse } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { AuthService } from '../../../core/auth/auth.service';
import { DemoPanelComponent } from './demo-panel.component';

describe('DemoPanelComponent', () => {
  const auth = { startDemoCustomer: vi.fn(), startDemoAdmin: vi.fn() };

  beforeEach(() => {
    auth.startDemoCustomer.mockReset();
    auth.startDemoAdmin.mockReset();
    TestBed.configureTestingModule({
      imports: [DemoPanelComponent],
      providers: [provideRouter([]), { provide: AuthService, useValue: auth }],
    });
  });

  const buttons = (el: HTMLElement) => Array.from(el.querySelectorAll<HTMLButtonElement>('button'));

  it('clicking Try as customer calls startDemoCustomer and navigates home', async () => {
    auth.startDemoCustomer.mockResolvedValue(undefined);
    const nav = vi.spyOn(TestBed.inject(Router), 'navigateByUrl').mockResolvedValue(true);
    const f = TestBed.createComponent(DemoPanelComponent);
    await f.whenStable();
    const el = f.nativeElement as HTMLElement;
    expect(el.textContent).toContain('Try it out');
    buttons(el)
      .find((b) => b.textContent?.includes('Try as customer'))!
      .click();
    await f.whenStable();
    expect(auth.startDemoCustomer).toHaveBeenCalledTimes(1);
    expect(nav).toHaveBeenCalledWith('/');
  });

  it('describes the admin account honestly (no nightly reset claim)', async () => {
    const f = TestBed.createComponent(DemoPanelComponent);
    await f.whenStable();
    const text = ((f.nativeElement as HTMLElement).textContent ?? '').replace(/\s+/g, ' ');
    expect(text).toContain('Shared admin account — admin tools are coming soon.');
    expect(text).not.toContain('nightly');
  });

  it('disables both buttons while a request is in flight', async () => {
    auth.startDemoAdmin.mockReturnValue(new Promise(() => undefined));
    const f = TestBed.createComponent(DemoPanelComponent);
    await f.whenStable();
    const el = f.nativeElement as HTMLElement;
    buttons(el)
      .find((b) => b.textContent?.includes('Try as admin'))!
      .click();
    await f.whenStable();
    expect(buttons(el).every((b) => b.disabled)).toBe(true);
    expect(el.querySelector('mat-spinner')).not.toBeNull();
  });

  it('429 shows rate-limit message', async () => {
    auth.startDemoCustomer.mockRejectedValue(
      new HttpErrorResponse({ status: 429, error: { type: 'rate_limited' } }),
    );
    const f = TestBed.createComponent(DemoPanelComponent);
    await f.whenStable();
    const el = f.nativeElement as HTMLElement;
    buttons(el)
      .find((b) => b.textContent?.includes('Try as customer'))!
      .click();
    await f.whenStable();
    expect(el.querySelector('[role="alert"]')?.textContent).toContain(
      'Too many demo sign-ins from your network — please wait a minute.',
    );
    expect(buttons(el).some((b) => b.disabled)).toBe(false);
  });
});
