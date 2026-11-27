import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { AdminApi } from './admin-api.service';

describe('AdminApi', () => {
  let http: HttpTestingController;
  let api: AdminApi;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    http = TestBed.inject(HttpTestingController);
    api = TestBed.inject(AdminApi);
  });
  afterEach(() => http.verify());

  it('lists orders, sending the filters that are set', () => {
    api.orders({ status: 'Paid', q: 'ada', page: 2, pageSize: 20 }).subscribe();
    const req = http.expectOne((r) => r.url === '/api/ordering/admin/orders');
    expect(req.request.params.get('status')).toBe('Paid');
    expect(req.request.params.get('q')).toBe('ada');
    expect(req.request.params.get('page')).toBe('2');
    expect(req.request.params.get('pageSize')).toBe('20');
    req.flush({});
  });

  it('omits empty filters', () => {
    api.orders({ status: '', q: '', page: 1, pageSize: 20 }).subscribe();
    const req = http.expectOne((r) => r.url === '/api/ordering/admin/orders');
    expect(req.request.params.has('status')).toBe(false);
    expect(req.request.params.has('q')).toBe(false);
    req.flush({});
  });

  it('gets one order and posts ship / deliver', () => {
    api.order('o 1').subscribe();
    http.expectOne('/api/ordering/admin/orders/o%201').flush({});
    api.ship('o1').subscribe();
    const ship = http.expectOne('/api/ordering/admin/orders/o1/ship');
    expect(ship.request.method).toBe('POST');
    ship.flush({});
    api.deliver('o1').subscribe();
    const deliver = http.expectOne('/api/ordering/admin/orders/o1/deliver');
    expect(deliver.request.method).toBe('POST');
    deliver.flush({});
  });
});
