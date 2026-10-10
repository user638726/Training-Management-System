import { HttpErrorResponse } from '@angular/common/http';
import { Component, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule, NgForm } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { finalize } from 'rxjs';
import { AuthService, safeReturnUrl } from './auth.service';
import { IconComponent } from './icon.component';

@Component({
  selector: 'app-register', standalone: true, imports: [FormsModule, RouterLink, IconComponent],
  template: `
    <main class="login-page register-page">
      <section class="login-story" aria-label="育成企業訓練管理">
        <div class="login-story-grain" aria-hidden="true"></div>
        <a class="brand login-brand" routerLink="/login" [queryParams]="{ returnUrl }"><span class="brand-mark"><app-icon name="book" /></span><span class="brand-name">育成<span>企業訓練管理</span></span></a>
        <div class="login-story-content">
          <div class="login-story-eyebrow"><span></span>GROW TOGETHER</div>
          <h1>加入學習的行列，<br>看見團隊的每份成長。</h1>
          <p>從課程到學習成果，掌握完整訓練資訊。<br>建立您的帳號，開啟企業學習工作台。</p>
          <div class="login-illustration" aria-hidden="true"><div class="login-orbit orbit-large"></div><div class="login-orbit orbit-small"></div><div class="login-step login-step-one"></div><div class="login-step login-step-two"></div><div class="login-step login-step-three"><app-icon name="sprout" /></div><span class="login-spark login-spark-one">✦</span><span class="login-spark login-spark-two">✧</span><div class="login-learning-card"><span><app-icon name="graduate" /></span><div><strong>每一份學習，都值得看見。</strong><small>讓知識成為團隊前進的力量</small></div></div></div>
        </div>
        <div class="login-story-footer"><span><app-icon name="book" />課程規劃</span><span><app-icon name="users" />人才發展</span><span><app-icon name="chart" />學習成果</span></div>
      </section>
      <section class="login-form-side" aria-labelledby="register-title">
        <div class="login-form-container register-form-container">
          <div class="login-mobile-brand"><span class="brand-mark"><app-icon name="book" /></span><strong>育成<span>企業訓練管理</span></strong></div>
          <span class="login-kicker">BEGIN YOUR LEARNING JOURNEY</span>
          <h2 id="register-title">建立您的帳號</h2>
          <p class="login-description">加入企業學習工作台，<br>一起掌握團隊成長的每一步。</p>
          @if (error()) { <div class="login-error" id="register-error" role="alert"><app-icon name="alert" /><span>{{error()}}</span></div> }
          <form #registerForm="ngForm" class="login-form register-form" (ngSubmit)="submit(registerForm)" [attr.aria-busy]="submitting()">
            <fieldset [disabled]="submitting()">
              <label class="login-field" for="register-username">帳號<input id="register-username" name="username" [(ngModel)]="username" required minlength="3" maxlength="80" pattern="[A-Za-z0-9._-]{3,80}" autocomplete="username" autocapitalize="none" spellcheck="false" placeholder="例如：jane.chen" aria-describedby="username-hint"><small id="username-hint" class="register-field-hint">3–80 個字元，可使用英文字母、數字及 . _ -</small></label>
              <label class="login-field" for="display-name">顯示名稱<input id="display-name" name="displayName" [(ngModel)]="displayName" required maxlength="100" autocomplete="name" placeholder="請輸入您的姓名或顯示名稱"></label>
              <label class="login-field" for="register-password">密碼<div class="password-field"><input id="register-password" name="password" [(ngModel)]="password" [type]="showPassword() ? 'text' : 'password'" required minlength="12" maxlength="256" autocomplete="new-password" placeholder="請設定至少 12 個字元的密碼" aria-describedby="password-hint"><button type="button" class="icon-button password-toggle" (click)="showPassword.set(!showPassword())" [attr.aria-label]="showPassword() ? '隱藏密碼' : '顯示密碼'" [attr.aria-pressed]="showPassword()"><app-icon [name]="showPassword() ? 'eye-off' : 'eye'" /></button></div><small id="password-hint" class="register-field-hint">12–256 個字元，請使用不易被猜到的密碼。</small></label>
              <label class="login-field" for="confirm-password">確認密碼<div class="password-field"><input id="confirm-password" name="confirmPassword" [(ngModel)]="confirmPassword" [type]="showConfirmPassword() ? 'text' : 'password'" required minlength="12" maxlength="256" autocomplete="new-password" placeholder="請再次輸入相同密碼"><button type="button" class="icon-button password-toggle" (click)="showConfirmPassword.set(!showConfirmPassword())" [attr.aria-label]="showConfirmPassword() ? '隱藏確認密碼' : '顯示確認密碼'" [attr.aria-pressed]="showConfirmPassword()"><app-icon [name]="showConfirmPassword() ? 'eye-off' : 'eye'" /></button></div></label>
              <button class="button primary login-submit" type="submit">@if (submitting()) { <span class="loading-spinner"></span>正在建立帳號… } @else { 建立帳號<app-icon name="arrow" /> }</button>
            </fieldset>
          </form>
          <p class="auth-switch">已經有帳號？<a routerLink="/login" [queryParams]="{ returnUrl }">返回登入<app-icon name="arrow" /></a></p>
          <div class="login-help register-help"><app-icon name="shield" /><div><strong>一般使用者帳號</strong><p>註冊後，帳號會出現在管理員的「員工管理 → 註冊帳號」清單。<br>管理員核對資料並為您建立新員工後，即可自行報名課程。</p></div></div>
        </div>
        <footer class="login-footer"><span>育成 · 企業訓練管理</span><span>每一份成長，都值得被看見。</span></footer>
      </section>
    </main>
  `
})
export class RegisterComponent {
  private auth = inject(AuthService);
  private router = inject(Router);
  private route = inject(ActivatedRoute);
  private destroyRef = inject(DestroyRef);
  readonly returnUrl = safeReturnUrl(this.route.snapshot.queryParamMap.get('returnUrl'));
  username = '';
  displayName = '';
  password = '';
  confirmPassword = '';
  showPassword = signal(false);
  showConfirmPassword = signal(false);
  submitting = signal(false);
  error = signal('');

  constructor() {
    if (this.auth.user() || this.auth.hasSession()) void this.router.navigate(['/login'], { queryParams: { returnUrl: this.returnUrl }, replaceUrl: true });
  }

  submit(form: NgForm) {
    if (this.submitting()) return;
    form.control.markAllAsTouched();
    if (!/^[A-Za-z0-9._-]{3,80}$/.test(this.username.trim())) { this.fail('帳號須為 3–80 個字元，僅可使用英文字母、數字及 . _ -。', 'register-username'); return; }
    if (!this.displayName.trim() || this.displayName.trim().length > 100) { this.fail('請輸入 1–100 個字元的顯示名稱。', 'display-name'); return; }
    if (this.password.length < 12 || this.password.length > 256 || !this.password.trim()) { this.fail('密碼須為 12–256 個字元，且不可僅包含空白。', 'register-password'); return; }
    if (this.confirmPassword !== this.password) { this.fail('確認密碼與密碼不一致，請再確認一次。', 'confirm-password'); return; }
    if (!form.valid) { this.error.set('請確認所有欄位已完整填寫且符合格式。'); return; }
    this.submitting.set(true);
    this.error.set('');
    this.auth.register({ username: this.username, displayName: this.displayName, password: this.password, confirmPassword: this.confirmPassword })
      .pipe(takeUntilDestroyed(this.destroyRef), finalize(() => this.submitting.set(false))).subscribe({
        next: user => {
          this.password = ''; this.confirmPassword = '';
          void this.router.navigate(['/login'], { queryParams: { reason: 'registered', username: user.username, returnUrl: this.returnUrl }, replaceUrl: true });
        },
        error: (error: unknown) => {
          if (error instanceof HttpErrorResponse) {
            if (error.status === 409) { this.error.set('此帳號已被使用，請換一個帳號。'); return; }
            if (error.status === 429) { this.error.set('註冊嘗試次數過多，請稍候再試。'); return; }
            if (error.status === 400) {
              const validation: unknown[] = error.error && typeof error.error === 'object' && error.error.errors ? Object.values(error.error.errors).flat() : [];
              const details = validation.filter((value): value is string => typeof value === 'string');
              this.error.set(details.length ? details.join(' ') : error.error?.detail || error.error?.title || '註冊資料不符合格式，請確認各欄位。');
              return;
            }
          }
          this.error.set('目前無法連線至註冊服務，請稍後再試。');
        }
      });
  }

  private fail(message: string, field: string) { this.error.set(message); document.getElementById(field)?.focus(); }
}
