import { Component, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { AuthService } from './auth.service';
import { IconComponent } from './icon.component';

@Component({
  selector: 'app-shell', standalone: true, imports: [RouterLink, RouterLinkActive, RouterOutlet, IconComponent],
  template: `
    @if (auth.user(); as user) {
      <a class="skip-link" href="#main-content">跳至主要內容</a>
      @if (mobileOpen()) { <button class="sidebar-scrim" aria-label="關閉導覽選單" (click)="mobileOpen.set(false)"></button> }
      <aside class="sidebar" [class.mobile-open]="mobileOpen()" aria-label="主要導覽">
        <a class="brand" routerLink="/overview" (click)="mobileOpen.set(false)"><span class="brand-mark"><app-icon name="book" /></span><span class="brand-name">育成<span>企業訓練管理</span></span></a>
        <div class="workspace-label"><span class="workspace-dot"></span>企業學習工作台<span class="workspace-tag">{{auth.canManage() ? '管理端' : '檢視端'}}</span></div>
        <div class="nav-caption">工作空間</div>
        <nav>
          @for (item of navigation; track item.path) {
            <a [routerLink]="item.path" routerLinkActive="active" ariaCurrentWhenActive="page" (click)="mobileOpen.set(false)"><app-icon [name]="item.icon" /><span>{{item.label}}</span><span class="nav-indicator"></span></a>
          }
        </nav>
        <div class="sidebar-note"><div class="note-symbol"><app-icon name="sprout" /></div><strong>讓每一次學習，<br>成為成長的起點。</strong><p>串起課程、人才與學習成果，<br>打造持續成長的團隊。</p></div>
        <div class="sidebar-footer auth-sidebar-footer"><span class="avatar" aria-hidden="true">{{auth.initials()}}</span><span class="sidebar-user"><strong>{{user.displayName}}</strong><small>{{auth.roleLabel()}}</small></span><button class="icon-button sidebar-logout" [disabled]="auth.logoutBusy()" (click)="logout()" [attr.aria-label]="auth.logoutBusy() ? '正在登出' : '登出帳號'" [attr.title]="auth.logoutBusy() ? '正在登出' : '登出帳號'"><app-icon name="logout" /></button></div>
      </aside>
      <div class="main-shell">
        <div class="topbar"><div class="topbar-left"><button class="icon-button mobile-menu" aria-label="開啟導覽選單" (click)="mobileOpen.set(true)"><app-icon name="menu" /></button><span class="breadcrumb">企業工作空間<span>/</span><strong>學習與發展</strong></span></div><div class="topbar-right"><span class="workspace-pill"><span></span>內部訓練</span><span class="topbar-user">{{user.displayName}}</span><span class="topbar-avatar" aria-hidden="true">{{auth.initials()}}</span><button class="icon-button topbar-logout" [disabled]="auth.logoutBusy()" (click)="logout()" aria-label="登出帳號" title="登出帳號"><app-icon name="logout" /></button></div></div>
        <main id="main-content"><router-outlet /></main>
        <footer class="page-footer"><span>育成 · 企業訓練管理</span><span>每一份成長，都值得被看見。</span></footer>
      </div>
      @if (auth.logoutBusy()) { <div class="logout-overlay" role="status"><span class="loading-spinner"></span><strong>正在登出帳號…</strong></div> }
    } @else {
      <div class="auth-transition" role="status"><span class="loading-spinner"></span><strong>正在返回登入頁面…</strong></div>
    }
  `
})
export class ShellComponent {
  auth = inject(AuthService);
  private router = inject(Router);
  private destroyRef = inject(DestroyRef);
  mobileOpen = signal(false);
  navigation = [
    { path: '/overview', label: '總覽儀表板', icon: 'overview' },
    { path: '/courses', label: '課程管理', icon: 'book' },
    { path: '/employees', label: '員工管理', icon: 'users' },
    { path: '/enrollments', label: '報名與學習紀錄', icon: 'graduate' },
    { path: '/reports', label: '訓練報表', icon: 'chart' }
  ];
  logout() {
    if (this.auth.logoutBusy()) return;
    this.mobileOpen.set(false);
    this.auth.logout().pipe(takeUntilDestroyed(this.destroyRef)).subscribe(() => {
      void this.router.navigate(['/login'], { queryParams: { reason: this.auth.notice() }, replaceUrl: true });
    });
  }
}
