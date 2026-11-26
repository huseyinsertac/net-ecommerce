import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { Router, RouterLink } from '@angular/router';
import { AuthService } from '../../core/auth/auth.service';

type Field = 'displayName' | 'email' | 'password';

interface ProblemBody {
  type?: string;
  errors?: Record<string, string[]>;
}

@Component({
  selector: 'app-register',
  imports: [
    ReactiveFormsModule,
    RouterLink,
    MatButtonModule,
    MatFormFieldModule,
    MatInputModule,
    MatProgressSpinnerModule,
  ],
  templateUrl: './register.page.html',
  styleUrl: '../auth-page.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class RegisterPage {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);

  protected readonly form = inject(FormBuilder).nonNullable.group({
    displayName: ['', [Validators.required, Validators.maxLength(100)]],
    email: ['', [Validators.required, Validators.email]],
    password: [
      '',
      [Validators.required, Validators.minLength(8), Validators.pattern(/^(?=.*[A-Za-z])(?=.*\d).+$/)],
    ],
  });
  protected readonly busy = signal(false);

  constructor() {
    // Editing a field drops its stale server message.
    for (const f of ['displayName', 'email', 'password'] as const) {
      this.form.controls[f].valueChanges.pipe(takeUntilDestroyed()).subscribe(() => {
        if (this.serverErrors()[f]) this.serverErrors.update((errs) => ({ ...errs, [f]: undefined }));
      });
    }
  }
  /** Messages from the server, per field. */
  protected readonly serverErrors = signal<Partial<Record<Field, string>>>({});
  protected readonly error = signal('');

  protected async submit(): Promise<void> {
    this.form.markAllAsTouched();
    if (this.form.invalid || this.busy()) return;
    this.busy.set(true);
    this.error.set('');
    this.serverErrors.set({});
    const { email, password, displayName } = this.form.getRawValue();
    try {
      await this.auth.register(email.trim(), password, displayName.trim());
      await this.router.navigateByUrl('/');
    } catch (err) {
      this.handle(err);
    } finally {
      this.busy.set(false);
    }
  }

  /** Shows server messages on their fields; mat-error only renders for controls in an error state. */
  private fail(fields: Partial<Record<Field, string>>): void {
    this.serverErrors.set(fields);
    for (const f of Object.keys(fields) as Field[]) this.form.controls[f].setErrors({ server: true });
  }

  private handle(err: unknown): void {
    if (!(err instanceof HttpErrorResponse)) {
      this.error.set('Could not create the account. Please try again.');
      return;
    }
    const body = (err.error ?? {}) as ProblemBody;
    if (err.status === 409 || body.type === 'identity.email_taken') {
      this.fail({ email: 'An account with this email already exists.' });
    } else if (body.type === 'identity.weak_password') {
      this.fail({ password: 'Use at least 8 characters, a letter and a digit.' });
    } else if (err.status === 400 && body.errors) {
      const fields: Partial<Record<Field, string>> = {};
      for (const f of ['displayName', 'email', 'password'] as const) {
        const first = body.errors[f]?.[0];
        if (first) fields[f] = first;
      }
      this.fail(fields);
      if (Object.keys(fields).length === 0) this.error.set('Please check the form and try again.');
    } else if (err.status === 429) {
      this.error.set('Too many attempts. Please wait a minute and try again.');
    } else if (err.status === 0) {
      this.error.set('Cannot reach the server. Check your connection and try again.');
    } else {
      this.error.set('Could not create the account. Please try again.');
    }
  }
}
