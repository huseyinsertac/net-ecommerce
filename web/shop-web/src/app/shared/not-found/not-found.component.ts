import { ChangeDetectionStrategy, Component } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { RouterLink } from '@angular/router';

@Component({
  selector: 'app-not-found',
  imports: [RouterLink, MatButtonModule],
  template: `
    <section class="nf">
      <h1>Page not found</h1>
      <p>We could not find what you were looking for.</p>
      <a mat-flat-button routerLink="/">Back to shop</a>
    </section>
  `,
  styles: `
    .nf {
      max-width: 480px;
      margin: 64px auto;
      padding: 0 16px;
      text-align: center;
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class NotFoundComponent {}
