import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { MatBadgeModule } from '@angular/material/badge';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatMenuModule } from '@angular/material/menu';
import { MatToolbarModule } from '@angular/material/toolbar';
import { NavigationEnd, Router, RouterLink } from '@angular/router';
import { filter, map, startWith } from 'rxjs';
import { BRAND } from '../../brand';

@Component({
  selector: 'app-header',
  imports: [
    RouterLink,
    MatBadgeModule,
    MatButtonModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    MatMenuModule,
    MatToolbarModule,
  ],
  templateUrl: './header.component.html',
  styleUrl: './header.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class HeaderComponent {
  protected readonly brand = BRAND;
  protected readonly cartCount = 0;
  private readonly router = inject(Router);
  /** Phone layout only: whether the search row is expanded. */
  protected readonly searchOpen = signal(false);

  /** Current `q` while on /search, else empty. */
  protected readonly currentQuery = toSignal(
    this.router.events.pipe(
      filter((e) => e instanceof NavigationEnd),
      startWith(null),
      map(() => {
        const tree = this.router.parseUrl(this.router.url);
        const onSearch = tree.root.children['primary']?.segments[0]?.path === 'search';
        return onSearch ? (tree.queryParams['q'] ?? '') : '';
      }),
    ),
    { initialValue: '' },
  );

  protected search(event: Event, value: string): void {
    event.preventDefault();
    const q = value.trim();
    if (!q) return;
    this.searchOpen.set(false);
    void this.router.navigate(['/search'], { queryParams: { q } });
  }
}
