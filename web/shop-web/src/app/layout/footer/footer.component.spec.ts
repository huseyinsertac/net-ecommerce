import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { FooterComponent } from './footer.component';

describe('FooterComponent', () => {
  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [FooterComponent],
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
  });

  it('footer shows API version from /api/system/info', async () => {
    const fixture = TestBed.createComponent(FooterComponent);
    const http = TestBed.inject(HttpTestingController);
    http
      .expectOne('/api/system/info')
      .flush({ name: 'VoltLane', version: '1.2.3', environment: 'Test' });
    await fixture.whenStable();
    fixture.detectChanges();
    expect((fixture.nativeElement as HTMLElement).textContent).toContain('API 1.2.3');
  });

  it('links to the photo credits', async () => {
    const fixture = TestBed.createComponent(FooterComponent);
    TestBed.inject(HttpTestingController).expectOne('/api/system/info').flush({});
    await fixture.whenStable();
    const link = (fixture.nativeElement as HTMLElement).querySelector<HTMLAnchorElement>('a.credits')!;
    expect(link.textContent).toContain('Photo credits');
    expect(link.getAttribute('href')).toBe('/images/CREDITS.md');
  });

  it('footer shows "API offline" when the request fails', async () => {
    const fixture = TestBed.createComponent(FooterComponent);
    const http = TestBed.inject(HttpTestingController);
    http.expectOne('/api/system/info').flush('boom', { status: 500, statusText: 'Server Error' });
    await fixture.whenStable();
    fixture.detectChanges();
    expect((fixture.nativeElement as HTMLElement).textContent).toContain('API offline');
  });
});
