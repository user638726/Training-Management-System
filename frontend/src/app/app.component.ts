import { Component, inject } from '@angular/core';
import { Router, RouterOutlet } from '@angular/router';
import { AuthService } from './auth.service';
import { IconComponent } from './icon.component';

@Component({
  selector: 'app-root', standalone: true, imports: [RouterOutlet, IconComponent],
  template: `
    <router-outlet />
    @if (auth.validating() && !router.url.startsWith('/login')) {
      <div class="auth-validating-screen" role="status">
        <span class="auth-loading-brand"><app-icon name="book" /></span>
        <strong>正在驗證登入狀態</strong>
        <p>確認您的帳號後，即可進入學習工作台。</p>
        <span class="loading-spinner"></span>
      </div>
    }
  `
})
export class AppComponent {
  auth = inject(AuthService);
  router = inject(Router);
}
