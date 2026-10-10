import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { DestroyRef, Injectable, computed, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { Observable, catchError, finalize, map, of, shareReplay, tap, timeout } from 'rxjs';

export interface AuthUser {
  id: number | string;
  username: string;
  displayName: string;
  role: string;
  employeeId: number | null;
}

interface AuthSession {
  accessToken: string;
  expiresAt: string;
}

interface LoginResponse extends AuthSession {
  tokenType: 'Bearer';
  user: AuthUser;
}

export type SessionValidation = 'authenticated' | 'unauthenticated' | 'unavailable';
export type SessionNotice = 'expired' | 'unauthorized' | 'unavailable' | 'logout' | 'logout-unavailable' | 'registered' | null;

const STORAGE_KEY = 'yucheng.auth.session';
const PROTECTED_PATHS = new Set(['/overview', '/courses', '/employees', '/enrollments', '/reports']);

// Only known internal pages can be used as a post-login destination.
export function safeReturnUrl(value: string | null | undefined): string {
  const path = (value ?? '').split(/[?#]/, 1)[0];
  return PROTECTED_PATHS.has(path) ? path : '/overview';
}

@Injectable({ providedIn: 'root' })
export class AuthService {
  private http = inject(HttpClient);
  private router = inject(Router);
  private destroyRef = inject(DestroyRef);
  private session = signal<AuthSession | null>(null);
  private verifiedToken: string | null = null;
  private validation$: Observable<SessionValidation> | null = null;
  private validationToken: string | null = null;
  private expiryTimer?: ReturnType<typeof setTimeout>;

  readonly user = signal<AuthUser | null>(null);
  readonly notice = signal<SessionNotice>(null);
  readonly logoutBusy = signal(false);
  readonly validating = signal(false);
  readonly hasSession = computed(() => this.session() !== null);
  readonly canManage = computed(() => this.user()?.role === 'Admin');
  readonly roleLabel = computed(() => {
    const role = this.user()?.role;
    return role === 'Admin' ? '系統管理員' : role === 'HR' ? '人資管理員' : role === 'Employee' ? '一般使用者' : role || '';
  });
  readonly initials = computed(() => {
    const name = this.user()?.displayName.trim() || this.user()?.username || '';
    return /^[\x00-\x7F]+$/.test(name) ? name.slice(0, 2).toUpperCase() : name.slice(-2);
  });

  constructor() {
    this.session.set(this.readSession());
    this.scheduleExpiration();
    const checkOnResume = () => { if (document.visibilityState === 'visible') this.accessToken(); };
    document.addEventListener('visibilitychange', checkOnResume);
    this.destroyRef.onDestroy(() => {
      clearTimeout(this.expiryTimer);
      document.removeEventListener('visibilitychange', checkOnResume);
    });
  }

  accessToken(): string | null {
    const session = this.session();
    if (!session) return null;
    if (Date.parse(session.expiresAt) <= Date.now()) {
      this.invalidate('expired');
      return null;
    }
    return session.accessToken;
  }

  ensureSession(): Observable<SessionValidation> {
    const token = this.accessToken();
    if (!token) return of('unauthenticated');
    if (this.verifiedToken === token && this.user()) return of('authenticated');
    if (this.validation$ && this.validationToken === token) return this.validation$;

    this.validationToken = token;
    this.validating.set(true);
    this.validation$ = this.http.get<AuthUser>('/api/auth/me').pipe(
      timeout(10000),
      map(user => {
        if (this.accessToken() !== token) return 'unauthenticated' as const;
        this.user.set(user);
        this.verifiedToken = token;
        this.notice.set(null);
        return 'authenticated' as const;
      }),
      catchError((error: unknown) => {
        if (this.session()?.accessToken !== token) return of(this.user() && this.session() ? 'authenticated' as const : 'unauthenticated' as const);
        if (error instanceof HttpErrorResponse && error.status === 401) {
          this.clearSession();
          this.notice.set('unauthorized');
          return of('unauthenticated' as const);
        }
        this.notice.set('unavailable');
        return of('unavailable' as const);
      }),
      finalize(() => {
        if (this.validationToken === token) {
          this.validation$ = null;
          this.validationToken = null;
          this.validating.set(false);
        }
      }),
      shareReplay({ bufferSize: 1, refCount: true })
    );
    return this.validation$;
  }

  login(username: string, password: string): Observable<LoginResponse> {
    return this.http.post<LoginResponse>('/api/auth/login', { username: username.trim(), password }).pipe(
      timeout(15000),
      tap(response => {
        const expiry = Date.parse(response.expiresAt);
        if (response.tokenType !== 'Bearer' || !response.accessToken || !Number.isFinite(expiry) || expiry <= Date.now() || !response.user) {
          throw new Error('Invalid authentication response');
        }
        this.session.set({ accessToken: response.accessToken, expiresAt: response.expiresAt });
        this.verifiedToken = response.accessToken;
        this.user.set(response.user);
        this.notice.set(null);
        this.persistSession();
        this.scheduleExpiration();
      })
    );
  }

  refreshProfile(): Observable<AuthUser> {
    return this.http.get<AuthUser>('/api/auth/me').pipe(tap(user => this.user.set(user)));
  }

  logout(): Observable<boolean> {
    this.logoutBusy.set(true);
    return this.http.post<void>('/api/auth/logout', {}).pipe(
      timeout(10000),
      map(() => true),
      catchError((error: unknown) => of(error instanceof HttpErrorResponse && error.status === 401)),
      tap(serverLogoutSucceeded => {
        this.clearSession();
        this.notice.set(serverLogoutSucceeded ? 'logout' : 'logout-unavailable');
      }),
      finalize(() => this.logoutBusy.set(false))
    );
  }

  handleUnauthorized() {
    if (this.session()) this.invalidate('unauthorized');
  }

  register(input: { username: string; displayName: string; password: string; confirmPassword: string }): Observable<AuthUser> {
    return this.http.post<AuthUser>('/api/auth/register', { username: input.username.trim(), displayName: input.displayName.trim(), password: input.password, confirmPassword: input.confirmPassword }).pipe(
      timeout(15000),
      tap(() => this.notice.set('registered'))
    );
  }

  private invalidate(reason: 'expired' | 'unauthorized') {
    const returnUrl = safeReturnUrl(this.router.url);
    this.clearSession();
    this.notice.set(reason);
    if (!this.router.url.startsWith('/login')) {
      void this.router.navigate(['/login'], { queryParams: { returnUrl, reason }, replaceUrl: true });
    }
  }

  private scheduleExpiration() {
    clearTimeout(this.expiryTimer);
    const session = this.session();
    if (!session) return;
    const remaining = Date.parse(session.expiresAt) - Date.now();
    if (remaining <= 0) { this.invalidate('expired'); return; }
    this.expiryTimer = setTimeout(() => {
      if (this.accessToken()) this.scheduleExpiration();
    }, Math.min(remaining, 2_147_483_647));
  }

  private clearSession() {
    clearTimeout(this.expiryTimer);
    this.session.set(null);
    this.user.set(null);
    this.verifiedToken = null;
    try { sessionStorage.removeItem(STORAGE_KEY); } catch { /* Storage may be unavailable in private browser contexts. */ }
  }

  private readSession(): AuthSession | null {
    try {
      const raw = sessionStorage.getItem(STORAGE_KEY);
      if (!raw) return null;
      const stored: unknown = JSON.parse(raw);
      if (!stored || typeof stored !== 'object' || !('accessToken' in stored) || !('expiresAt' in stored)
        || typeof stored.accessToken !== 'string' || !stored.accessToken || typeof stored.expiresAt !== 'string'
        || !Number.isFinite(Date.parse(stored.expiresAt))) {
        sessionStorage.removeItem(STORAGE_KEY);
        return null;
      }
      if (Date.parse(stored.expiresAt) <= Date.now()) {
        sessionStorage.removeItem(STORAGE_KEY);
        this.notice.set('expired');
        return null;
      }
      return { accessToken: stored.accessToken, expiresAt: stored.expiresAt };
    } catch {
      try { sessionStorage.removeItem(STORAGE_KEY); } catch { /* No persistent client state if browser storage is blocked. */ }
      return null;
    }
  }

  private persistSession() {
    try { sessionStorage.setItem(STORAGE_KEY, JSON.stringify(this.session())); } catch { /* The current in-memory session remains usable. */ }
  }
}
