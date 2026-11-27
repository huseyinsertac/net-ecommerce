import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { AdminShellComponent } from './admin-shell.component';

describe('AdminShellComponent', () => {
  function setup() {
    TestBed.configureTestingModule({ providers: [provideRouter([])] });
    const fixture = TestBed.createComponent(AdminShellComponent);
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  }

  it('has the top bar, demo notice and Orders / Products nav', () => {
    const el = setup();
    expect(el.textContent).toContain('Admin · shared demo account');
    expect(el.textContent).toContain(
      'Changes are visible to every visitor and reset nightly at 02:00 UTC.',
    );
    const links = [...el.querySelectorAll('nav a')].map((a) => a.textContent ?? '');
    expect(links.length).toBe(2);
    expect(links[0]).toContain('Orders');
    expect(links[1]).toContain('Products');
    expect(el.querySelector('nav a[href="/admin/orders"]')).not.toBeNull();
    expect(el.querySelector('nav a[href="/admin/products"]')).not.toBeNull();
  });

  it('has a menu button for the narrow layout', () => {
    expect(setup().querySelector('button[aria-label="Open admin menu"]')).not.toBeNull();
  });
});
