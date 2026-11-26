import {
  ChangeDetectionStrategy,
  Component,
  computed,
  ElementRef,
  Injector,
  afterNextRender,
  inject,
  signal,
  viewChild,
} from '@angular/core';
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
import { AuthService } from '../../core/auth/auth.service';
import { BasketService } from '../../core/basket/basket.service';

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
  protected readonly cart = inject(BasketService);
  protected readonly cartLabel = computed(() => {
    const n = this.cart.count();
    return n === 0 ? 'Cart' : `Cart, ${n} ${n === 1 ? 'item' : 'items'}`;
  });
  protected readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  /** Phone layout only: whether the search row is expanded. */
  protected readonly searchOpen = signal(false);
  private readonly injector = inject(Injector);
  private readonly searchInput = viewChild<ElementRef<HTMLInputElement>>('q');

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

  /** Opens/closes the phone search row; opening moves focus to the input after render. */
  protected toggleSearch(): void {
    const open = !this.searchOpen();
    this.searchOpen.set(open);
    if (open) {
      afterNextRender(() => this.searchInput()?.nativeElement.focus(), {
        injector: this.injector,
      });
    }
  }

  protected async signOut(): Promise<void> {
    await this.auth.logout();
    await this.router.navigateByUrl('/');
  }

  protected search(event: Event, value: string): void {
    event.preventDefault();
    const q = value.trim();
    if (!q) return;
    this.searchOpen.set(false);
    void this.router.navigate(['/search'], { queryParams: { q } });
  }
}
