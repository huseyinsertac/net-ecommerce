import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { RouterLink } from '@angular/router';
import { BRAND } from '../../brand';
import { AuthService } from '../../core/auth/auth.service';
import { CatalogApi } from '../../api/catalog-api.service';
import { DemoPanelComponent } from './demo-panel/demo-panel.component';
import { ProductCardComponent } from '../../shared/product-card/product-card.component';

@Component({
  selector: 'app-home',
  imports: [MatButtonModule, RouterLink, DemoPanelComponent, ProductCardComponent],
  templateUrl: './home.component.html',
  styleUrl: './home.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class HomeComponent {
  private readonly api = inject(CatalogApi);
  protected readonly auth = inject(AuthService);

  protected readonly brand = BRAND;
  protected readonly skeletons = Array.from({ length: 8 }, (_, i) => i);

  protected readonly categories = this.api.categories();
  protected readonly featured = this.api.products(
    signal({ featured: true, brands: [], inStock: false, specs: [], page: 1, pageSize: 8 }),
  );

  protected readonly categoryList = computed(() =>
    this.categories.hasValue() ? this.categories.value() : undefined,
  );
  protected readonly featuredItems = computed(() =>
    this.featured.hasValue() ? this.featured.value().items : undefined,
  );

  /** The API returns the full-size category image; tiles use the -400 thumbnail beside it. */
  protected thumb(url: string): string {
    return url.replace(/\.webp$/, '-400.webp');
  }
}
