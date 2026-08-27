import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { map } from 'rxjs';
import { AuthService } from '../auth/auth.service';
import { AuthStore } from '../auth/auth.store';

export const adminLoginGuard: CanActivateFn = () => {
  const store = inject(AuthStore);
  const router = inject(Router);
  if (store.isAuthenticated()) {
    return router.createUrlTree(['/admin']);
  }
  if (store.status() === 'anonymous') {
    return true;
  }
  return inject(AuthService)
    .restoreSession()
    .pipe(map((authenticated) => (authenticated ? router.createUrlTree(['/admin']) : true)));
};
