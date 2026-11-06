import { Component, DestroyRef, inject, signal } from '@angular/core';
import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { finalize } from 'rxjs';
@Component({ selector: 'app-invite', standalone: true, imports: [FormsModule, RouterLink], template: `
  <main class="invite-page"><section><a routerLink="/login">育成 · 企業訓練管理</a><h1>接受公司邀請</h1>
  @if (done()) { <p role="status">帳號已建立並通過核准，現在可以登入。</p><a class="button primary" routerLink="/login">前往登入</a> }
  @else { @if (error()) { <p class="error" role="alert">{{error()}}</p> }<form #form="ngForm" (ngSubmit)="accept()"><fieldset [disabled]="busy() || !token"><label>帳號<input name="username" [(ngModel)]="username" required minlength="3" maxlength="80" pattern="[A-Za-z0-9._-]+" autocomplete="username"></label><label>顯示名稱<input name="name" [(ngModel)]="displayName" required maxlength="100" autocomplete="name"></label><label>密碼（至少 12 字元）<input type="password" name="password" [(ngModel)]="password" required minlength="12" maxlength="256" autocomplete="new-password"></label><label>確認密碼<input type="password" name="confirm" [(ngModel)]="confirmPassword" required autocomplete="new-password"></label><button class="button primary" [disabled]="!form.valid || password !== confirmPassword">{{busy() ? '建立中…' : '接受邀請並建立帳號'}}</button></fieldset></form> }</section></main>`, styles: [`:host{display:block}.invite-page{min-height:100vh;display:grid;place-items:center;padding:24px;background:#f1f5ed}.invite-page section{width:min(100%,480px);padding:38px;border:1px solid #dce6d6;border-radius:20px;background:#fff}.invite-page h1{font-size:27px;color:#245d47;margin:24px 0}.invite-page label{display:flex;flex-direction:column;gap:8px;margin:18px 0;font-size:14px}.invite-page input{border:1px solid #d3dfd2;border-radius:8px;padding:11px}.invite-page fieldset{border:0;padding:0;min-width:0}.invite-page a{color:#306d53}.invite-page .error{color:#a74934}.invite-page .button{width:100%;justify-content:center}`] })
export class InviteComponent {
  private http = inject(HttpClient); private destroy = inject(DestroyRef);
  token = new URLSearchParams(location.hash.slice(1)).get('token') || '';
  username = ''; displayName = ''; password = ''; confirmPassword = ''; busy = signal(false); done = signal(false); error = signal('');
  constructor() { history.replaceState(null, '', location.pathname); if (!this.token) this.error.set('請使用管理員提供的完整邀請連結。'); }
  accept() { if (this.busy() || !this.token || this.password !== this.confirmPassword) return; this.busy.set(true); this.error.set('');
    this.http.post('/api/enterprise/invitations/accept', { token: this.token, username: this.username, displayName: this.displayName, password: this.password })
      .pipe(takeUntilDestroyed(this.destroy), finalize(() => this.busy.set(false))).subscribe({ next: () => { this.password = ''; this.confirmPassword = ''; this.token = ''; this.done.set(true); }, error: (e: HttpErrorResponse) => this.error.set(e.error?.title || '無法接受邀請，請稍後重試。') });
  }
}
