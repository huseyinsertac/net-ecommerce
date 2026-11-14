import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { MatSnackBar } from '@angular/material/snack-bar';
import { problemInterceptor } from './problem-interceptor';

describe('problemInterceptor', () => {
  let open: ReturnType<typeof vi.fn>;
  let http: HttpClient;
  let ctrl: HttpTestingController;

  beforeEach(() => {
    open = vi.fn();
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors([problemInterceptor])),
        provideHttpClientTesting(),
        { provide: MatSnackBar, useValue: { open } },
      ],
    });
    http = TestBed.inject(HttpClient);
    ctrl = TestBed.inject(HttpTestingController);
  });

  it('toasts the problem title on 500', () => {
    http.get('/x').subscribe({ error: () => undefined });
    ctrl
      .expectOne('/x')
      .flush({ title: 'Boom', status: 500 }, { status: 500, statusText: 'Server Error' });
    expect(open).toHaveBeenCalledWith('Boom', 'Dismiss', expect.anything());
  });

  it('toasts on network error (status 0)', () => {
    http.get('/x').subscribe({ error: () => undefined });
    ctrl.expectOne('/x').error(new ProgressEvent('error'));
    expect(open).toHaveBeenCalledTimes(1);
  });

  it('does not toast 404', () => {
    http.get('/x').subscribe({ error: () => undefined });
    ctrl
      .expectOne('/x')
      .flush({ title: 'Nope', type: 'catalog.product_not_found' }, { status: 404, statusText: 'NF' });
    expect(open).not.toHaveBeenCalled();
  });
});
