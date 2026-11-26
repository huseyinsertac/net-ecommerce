import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { csrfInterceptor } from './csrf.interceptor';

describe('csrfInterceptor', () => {
  let http: HttpClient;
  let ctrl: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors([csrfInterceptor])),
        provideHttpClientTesting(),
      ],
    });
    http = TestBed.inject(HttpClient);
    ctrl = TestBed.inject(HttpTestingController);
  });

  afterEach(() => ctrl.verify());

  const header = (url: string) => ctrl.expectOne(url).request.headers.get('X-Requested-With');

  it('adds X-Requested-With: voltlane to same-origin /api/ POST, PUT and DELETE', () => {
    http.post('/api/identity/refresh', null).subscribe();
    http.put('/api/basket/items/v1', { quantity: 1 }).subscribe();
    http.delete(`${location.origin}/api/basket`).subscribe();
    expect(header('/api/identity/refresh')).toBe('voltlane');
    expect(header('/api/basket/items/v1')).toBe('voltlane');
    expect(header(`${location.origin}/api/basket`)).toBe('voltlane');
  });

  it('leaves GET requests, non-API paths and other origins alone', () => {
    http.get('/api/basket').subscribe();
    http.post('/not-api/x', null).subscribe();
    http.post('https://other.example/api/x', null).subscribe();
    http.post('//other.example/api/y', null).subscribe();
    expect(header('/api/basket')).toBeNull();
    expect(header('/not-api/x')).toBeNull();
    expect(header('https://other.example/api/x')).toBeNull();
    expect(header('//other.example/api/y')).toBeNull();
  });
});
