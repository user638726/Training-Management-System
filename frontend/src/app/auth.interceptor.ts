import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { catchError, throwError } from 'rxjs';
import { AuthService } from './auth.service';

export const authInterceptor: HttpInterceptorFn = (request, next) => {
  const auth = inject(AuthService);
  const target = new URL(request.url, document.baseURI);
  const isAppApi = target.origin === location.origin && target.pathname.startsWith('/api/');
  const isPublicAuth = target.pathname === '/api/auth/login' || target.pathname === '/api/auth/register';
  const isLogout = target.pathname === '/api/auth/logout';
  if (!isAppApi || isPublicAuth) return next(request);
  const token = auth.accessToken();
  const authenticatedRequest = token ? request.clone({ setHeaders: { Authorization: `Bearer ${token}` } }) : request;
  return next(authenticatedRequest).pipe(catchError((error: unknown) => {
    if (error instanceof HttpErrorResponse && error.status === 401 && !isLogout && token && auth.accessToken() === token) auth.handleUnauthorized();
    return throwError(() => error);
  }));
};
