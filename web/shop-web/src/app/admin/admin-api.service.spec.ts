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

  it('lists products, sending only the filters that are set', () => {
    api.products({ status: 'Draft', q: '', page: 2, pageSize: 20 }).subscribe();
    const req = http.expectOne((r) => r.url === '/api/catalog/admin/products');
    expect(req.request.params.get('status')).toBe('Draft');
    expect(req.request.params.has('q')).toBe(false);
    expect(req.request.params.get('page')).toBe('2');
    req.flush({});
  });

  it('updates a product and a variant with PUT', () => {
    api.updateProduct('p1', { name: 'N', brand: 'B', description: 'D', isFeatured: false, imageKeys: ['a'], specs: {}, status: 'Draft', version: 3 }).subscribe();
    const product = http.expectOne('/api/catalog/admin/products/p1');
    expect(product.request.method).toBe('PUT');
    expect(product.request.body.version).toBe(3);
    product.flush({});

    api.updateVariant('p1', 'v1', { price: 5, stockOnHand: 2, version: 3 }).subscribe();
    const variant = http.expectOne('/api/catalog/admin/products/p1/variants/v1');
    expect(variant.request.method).toBe('PUT');
    variant.flush({});
  });
});
