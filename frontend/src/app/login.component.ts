import { HttpErrorResponse } from '@angular/common/http';
import { Component, DestroyRef, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule, NgForm } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { finalize } from 'rxjs';
import { AuthService, SessionNotice, safeReturnUrl } from './auth.service';
import { IconComponent } from './icon.component';

@Component({
  selector: 'app-login', standalone: true, imports: [FormsModule, IconComponent, RouterLink],
  template: `
    <main class="login-page">
      <section class="login-story" aria-label="育成企業訓練管理">
        <div class="login-story-grain" aria-hidden="true"></div>
        <a class="brand login-brand" href="/login"><span class="brand-mark"><app-icon name="book" /></span><span class="brand-name">育成<span>企業訓練管理</span></span></a>
        <div class="login-story-content">
          <div class="login-story-eyebrow"><span></span>GROW TOGETHER</div>
          <h1>人才的每一步成長，<br>都從學習開始。</h1>
          <p>連結課程、團隊與學習成果，<br>讓企業的人才發展，有方向，也有累積。</p>
          <div class="login-illustration" aria-hidden="true">
            <div class="login-orbit orbit-large"></div><div class="login-orbit orbit-small"></div>
            <div class="login-step login-step-one"></div><div class="login-step login-step-two"></div><div class="login-step login-step-three"><app-icon name="sprout" /></div>
            <span class="login-spark login-spark-one">✦</span><span class="login-spark login-spark-two">✧</span>
            <div class="login-learning-card"><span><app-icon name="graduate" /></span><div><strong>每一份學習，都值得看見。</strong><small>讓知識成為團隊前進的力量</small></div></div>
          </div>
        </div>
        <div class="login-story-footer"><span><app-icon name="book" />課程規劃</span><span><app-icon name="users" />人才發展</span><span><app-icon name="chart" />學習成果</span></div>
      </section>
      <section class="login-form-side" aria-labelledby="login-title">
        <div class="login-form-container">
          <div class="login-mobile-brand"><span class="brand-mark"><app-icon name="book" /></span><strong>育成<span>企業訓練管理</span></strong></div>
          <span class="login-kicker">YOUR LEARNING WORKSPACE</span>
          <h2 id="login-title">歡迎回到育成</h2>
          <p class="login-description">登入企業學習工作台，<br>為團隊安排下一次成長。</p>
          @if (noticeMessage()) { <div [class]="'login-notice ' + (noticeReason() === 'logout' || noticeReason() === 'registered' ? 'login-notice-success' : '')" role="status"><app-icon [name]="noticeReason() === 'logout' || noticeReason() === 'registered' ? 'circlecheck' : 'alert'" /><span>{{noticeMessage()}}</span></div> }
          @if (error()) { <div class="login-error" id="login-error" role="alert"><app-icon name="alert" /><span>{{error()}}</span></div> }
          @if (verifying()) { <div class="login-verifying" role="status"><span class="loading-spinner"></span>正在驗證您的登入狀態…</div> }
          <form #loginForm="ngForm" (ngSubmit)="submit(loginForm)" class="login-form" [attr.aria-busy]="submitting() || verifying()">
            <fieldset [disabled]="submitting() || verifying()">
              <label class="login-field" for="username">帳號<input id="username" name="username" [(ngModel)]="username" required maxlength="100" autocomplete="username" autocapitalize="none" spellcheck="false" placeholder="請輸入您的帳號" [attr.aria-describedby]="error() ? 'login-error' : null"></label>
              <label class="login-field" for="password">密碼<div class="password-field"><input id="password" name="password" [(ngModel)]="password" [type]="showPassword() ? 'text' : 'password'" required maxlength="256" autocomplete="current-password" placeholder="請輸入您的密碼" [attr.aria-describedby]="error() ? 'login-error' : null"><button type="button" class="icon-button password-toggle" (click)="showPassword.set(!showPassword())" [attr.aria-label]="showPassword() ? '隱藏密碼' : '顯示密碼'" [attr.aria-pressed]="showPassword()"><app-icon [name]="showPassword() ? 'eye-off' : 'eye'" /></button></div></label>
              <button type="submit" class="button primary login-submit">@if (submitting()) { <span class="loading-spinner"></span>正在登入… } @else { 登入工作台<app-icon name="arrow" /> }</button>
            </fieldset>
          </form>
          @if (auth.hasSession() && !auth.user() && !verifying()) { <button type="button" class="button secondary restore-session-button" [disabled]="submitting()" (click)="restoreSession()"><app-icon name="refresh" />重新驗證登入狀態</button> }
          <p class="auth-switch">還沒有帳號？<a routerLink="/register" [queryParams]="{ returnUrl }">建立帳號<app-icon name="arrow" /></a></p>
          <div class="login-help"><app-icon name="shield" /><div><strong>您的企業專屬學習空間</strong><p>請使用您的帳號登入。<br>如需帳號或密碼協助，請聯繫系統管理員。</p></div></div>
        </div>
        <footer class="login-footer"><span>育成 · 企業訓練管理</span><span>每一份成長，都值得被看見。</span></footer>
      </section>
    </main>
  `
})
export class LoginComponent {
  auth = inject(AuthService);
  private route = inject(ActivatedRoute);
  private router = inject(Router);
  private destroyRef = inject(DestroyRef);
  readonly returnUrl = safeReturnUrl(this.route.snapshot.queryParamMap.get('returnUrl'));
  private queryNotice = this.route.snapshot.queryParamMap.get('reason');
  username = /^[A-Za-z0-9._-]{3,80}$/.test(this.route.snapshot.queryParamMap.get('username') || '') ? this.route.snapshot.queryParamMap.get('username')! : '';
  password = '';
  showPassword = signal(false);
  submitting = signal(false);
  verifying = signal(false);
  error = signal('');
  noticeReason = computed<SessionNotice>(() => {
    const allowed: SessionNotice[] = ['expired', 'unauthorized', 'unavailable', 'logout', 'logout-unavailable', 'registered'];
    return this.auth.notice() ?? (allowed.includes(this.queryNotice as SessionNotice) ? this.queryNotice as SessionNotice : null);
  });
  noticeMessage = computed(() => {
    const messages: Record<string, string> = {
      expired: '登入時間已到期，請重新登入以繼續使用。',
      unauthorized: '登入狀態已失效，請重新登入以繼續使用。',
      unavailable: '目前無法驗證登入狀態，請稍後重新驗證或登入。',
      logout: '您已登出帳號，歡迎隨時回來繼續管理。',
      'logout-unavailable': '您已從此裝置登出；伺服器暫時無法連線完成登出驗證。',
      registered: '帳號已建立！管理員可在員工管理的「註冊帳號」清單核對資料並建立員工。請使用新帳號登入。'
    };
    return messages[this.noticeReason() || ''] || '';
  });

  constructor() {
    if (this.auth.user()) void this.router.navigateByUrl(this.returnUrl, { replaceUrl: true });
    else if (this.auth.hasSession() && this.noticeReason() !== 'unavailable') this.restoreSession();
  }

  submit(form: NgForm) {
    if (this.submitting() || this.verifying()) return;
    if (!form.valid || !this.username.trim()) {
      form.control.markAllAsTouched();
      this.error.set('請輸入帳號與密碼。');
      document.getElementById(!this.username.trim() ? 'username' : 'password')?.focus();
      return;
    }
    this.submitting.set(true);
    this.error.set('');
    this.auth.login(this.username, this.password).pipe(takeUntilDestroyed(this.destroyRef), finalize(() => this.submitting.set(false))).subscribe({
      next: () => { this.password = ''; void this.router.navigateByUrl(this.returnUrl, { replaceUrl: true }); },
      error: (error: unknown) => {
        this.error.set(error instanceof HttpErrorResponse && error.status === 401 ? '帳號或密碼不正確，請再試一次。'
          : error instanceof HttpErrorResponse && error.status === 429 ? '登入嘗試次數過多，請稍候再試。'
          : '目前無法連線至登入服務，請稍後再試。');
      }
    });
  }

  restoreSession() {
    if (this.verifying() || this.submitting()) return;
    this.verifying.set(true);
    this.error.set('');
    this.auth.ensureSession().pipe(takeUntilDestroyed(this.destroyRef), finalize(() => this.verifying.set(false))).subscribe(result => {
      if (result === 'authenticated') void this.router.navigateByUrl(this.returnUrl, { replaceUrl: true });
    });
  }
}
