import { Component, inject, signal } from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { finalize, take } from 'rxjs';
import { ApiHttpError } from '../../../core/api/api-error.model';
import { AuthService } from '../../../core/auth/auth.service';
import { InlineAlertComponent } from '../../../shared/components/inline-alert/inline-alert.component';
import { LoadingIndicatorComponent } from '../../../shared/components/loading-indicator/loading-indicator.component';

@Component({
  selector: 'app-login-page',
  imports: [ReactiveFormsModule, InlineAlertComponent, LoadingIndicatorComponent],
  templateUrl: './login-page.component.html',
  styleUrl: './login-page.component.css',
})
export class LoginPageComponent {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);

  readonly submitting = signal(false);
  readonly errorMessage = signal<string | null>(null);
  readonly form = new FormGroup({
    email: new FormControl('', {
      nonNullable: true,
      validators: [Validators.required, Validators.email, Validators.maxLength(320)],
    }),
    password: new FormControl('', {
      nonNullable: true,
      validators: [Validators.required, Validators.maxLength(256)],
    }),
  });

  submit(): void {
    if (this.form.invalid || this.submitting()) {
      this.form.markAllAsTouched();
      return;
    }
    this.submitting.set(true);
    this.errorMessage.set(null);
    this.auth
      .login(this.form.getRawValue())
      .pipe(take(1), finalize(() => this.submitting.set(false)))
      .subscribe({
        next: () => {
          this.form.controls.password.reset('');
          void this.router.navigateByUrl(this.safeReturnUrl());
        },
        error: (error: unknown) => {
          this.errorMessage.set(
            error instanceof ApiHttpError ? error.apiError.message : 'Unable to sign in. Please try again.',
          );
        },
      });
  }

  private safeReturnUrl(): string {
    const candidate = this.route.snapshot.queryParamMap.get('returnUrl');
    return candidate?.startsWith('/admin') && !candidate.startsWith('/admin/login') ? candidate : '/admin';
  }
}
