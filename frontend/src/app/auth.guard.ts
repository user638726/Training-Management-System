import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { map } from 'rxjs';
import { AuthService, safeReturnUrl } from './auth.service';

export const authGuard: CanActivateFn = (_route, state) => {
  const auth = inject(AuthService);
  const router = inject(Router);
  return auth.ensureSession().pipe(map(result => result === 'authenticated' ? true : router.createUrlTree(['/login'], {
    queryParams: { returnUrl: safeReturnUrl(state.url), ...(auth.notice() ? { reason: auth.notice() } : {}) }
  })));
};
