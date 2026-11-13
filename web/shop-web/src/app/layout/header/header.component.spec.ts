import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { BRAND } from '../../brand';
import { HeaderComponent } from './header.component';

describe('HeaderComponent', () => {
  it('header shows brand name from BRAND', async () => {
    TestBed.configureTestingModule({
      imports: [HeaderComponent],
      providers: [provideRouter([])],
    });
    const fixture = TestBed.createComponent(HeaderComponent);
    await fixture.whenStable();
    expect((fixture.nativeElement as HTMLElement).textContent).toContain(BRAND.name);
  });
});
