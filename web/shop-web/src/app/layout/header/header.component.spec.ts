import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { BRAND } from '../../brand';
import { HeaderComponent } from './header.component';

describe('HeaderComponent', () => {
  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [HeaderComponent],
      providers: [provideRouter([])],
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
});
