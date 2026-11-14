import { ChangeDetectionStrategy, Component } from '@angular/core';

/** Placeholder; implemented in Task 10. */
@Component({
  selector: 'app-product-list-page',
  template: `<p>Products</p>`,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ProductListPage {}
