import { inject } from '@angular/core';
import { CanActivateChildFn, CanActivateFn, Router } from '@angular/router';
import { map } from 'rxjs';
import { AuthService } from '../auth/auth.service';
import { AuthStore } from '../auth/auth.store';

export const adminAuthGuard: CanActivateFn = (_route, state) => {
  const store = inject(AuthStore);
  const router = inject(Router);
  if (store.isAuthenticated()) {
    return true;
  }
  if (store.status() === 'anonymous') {
    return router.createUrlTree(['/admin/login'], { queryParams: { returnUrl: state.url } });
  }
  return inject(AuthService)
    .restoreSession()
    .pipe(
      map((authenticated) =>
        authenticated
          ? true
          : router.createUrlTree(['/admin/login'], { queryParams: { returnUrl: state.url } }),
      ),
    );
};

export const adminAuthChildGuard: CanActivateChildFn = (route, state) =>
  adminAuthGuard(route, state);
