import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { Router } from '@angular/router';
import { AuthService } from '../../../core/auth/auth.service';

type Kind = 'customer' | 'admin';

@Component({
  selector: 'app-demo-panel',
  imports: [MatButtonModule, MatCardModule, MatIconModule, MatProgressSpinnerModule],
  templateUrl: './demo-panel.component.html',
  styleUrl: './demo-panel.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class DemoPanelComponent {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);

  /** Which demo is being started; both buttons are disabled while set. */
  protected readonly pending = signal<Kind | null>(null);
  protected readonly error = signal('');

  protected async start(kind: Kind): Promise<void> {
    if (this.pending()) return;
    this.pending.set(kind);
    this.error.set('');
    try {
      await (kind === 'customer' ? this.auth.startDemoCustomer() : this.auth.startDemoAdmin());
      await this.router.navigateByUrl('/');
    } catch (err) {
      this.error.set(messageFor(err));
    } finally {
      this.pending.set(null);
    }
  }
}

function messageFor(err: unknown): string {
  const status = err instanceof HttpErrorResponse ? err.status : -1;
  if (status === 429) return 'Too many demo sign-ins from your network — please wait a minute.';
  if (status === 503) return 'The admin demo is temporarily unavailable. Please try again later.';
  if (status === 0) return 'Cannot reach the server. Check your connection and try again.';
  return 'Could not start the demo. Please try again.';
}
