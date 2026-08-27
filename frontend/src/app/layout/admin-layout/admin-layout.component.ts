import { Component, inject, signal } from '@angular/core';
import { Router, RouterOutlet } from '@angular/router';
import { finalize } from 'rxjs';
import { AuthService } from '../../core/auth/auth.service';
import { AuthStore } from '../../core/auth/auth.store';
import { AdminSidebarComponent } from './admin-sidebar.component';
import { AdminTopbarComponent } from './admin-topbar.component';

@Component({
  selector: 'app-admin-layout',
  imports: [RouterOutlet, AdminSidebarComponent, AdminTopbarComponent],
  template: `
    <app-admin-sidebar [open]="navigationOpen()" (navigate)="navigationOpen.set(false)" />
    @if (navigationOpen()) { <button class="backdrop" type="button" (click)="navigationOpen.set(false)" aria-label="Close navigation"></button> }
    <div class="workspace">
      <app-admin-topbar [adminName]="store.admin()?.fullName ?? store.admin()?.email ?? null" [loggingOut]="loggingOut()" (menu)="navigationOpen.set(true)" (logout)="logout()" />
      <main><router-outlet /></main>
    </div>
  `,
  styles: `
    :host { display: block; min-height: 100vh; background: var(--color-background); }
    .workspace { min-height: 100vh; padding-left: 17.5rem; }
    main { min-height: calc(100vh - 4rem); }
    .backdrop { display: none; }
    @media (max-width: 900px) { .workspace { padding-left: 0; } .backdrop { display: block; position: fixed; inset: 0; z-index: 45; border: 0; background: #0009; } }
  `,
})
export class AdminLayoutComponent {
  readonly store = inject(AuthStore);
  readonly navigationOpen = signal(false);
  readonly loggingOut = signal(false);
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);

  logout(): void {
    if (this.loggingOut()) return;
    this.loggingOut.set(true);
    this.auth.logout().pipe(finalize(() => this.loggingOut.set(false))).subscribe({
      next: () => void this.router.navigate(['/admin/login']),
      error: () => void this.router.navigate(['/admin/login']),
    });
  }
}
