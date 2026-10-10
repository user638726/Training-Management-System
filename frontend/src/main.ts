import { bootstrapApplication } from '@angular/platform-browser';
import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { provideRouter, withInMemoryScrolling } from '@angular/router';
import { registerLocaleData } from '@angular/common';
import localeZh from '@angular/common/locales/zh-Hant';
import { LOCALE_ID, provideZonelessChangeDetection } from '@angular/core';
import { AppComponent } from './app/app.component';
import { WorkspaceComponent } from './app/workspace.component';
import { LoginComponent } from './app/login.component';
import { RegisterComponent } from './app/register.component';
import { ShellComponent } from './app/shell.component';
import { authGuard } from './app/auth.guard';
import { authInterceptor } from './app/auth.interceptor';

const sectionTitles: Record<string, string> = { overview: '總覽儀表板', courses: '課程管理', employees: '員工管理', enrollments: '報名與學習紀錄', reports: '訓練報表' };

registerLocaleData(localeZh);
bootstrapApplication(AppComponent, {
  providers: [
    provideZonelessChangeDetection(), provideHttpClient(withInterceptors([authInterceptor])), { provide: LOCALE_ID, useValue: 'zh-Hant' },
    provideRouter([
      { path: 'login', title: '育成｜登入企業學習工作台', component: LoginComponent },
      { path: 'register', title: '育成｜建立帳號', component: RegisterComponent },
      { path: '', component: ShellComponent, canActivate: [authGuard], canActivateChild: [authGuard], children: [
        { path: '', pathMatch: 'full', redirectTo: 'overview' },
        ...Object.keys(sectionTitles).map(section => ({ path: section, title: `育成｜${sectionTitles[section]}`, component: WorkspaceComponent, data: { section } }))
      ] },
      { path: '**', redirectTo: 'overview' }
    ], withInMemoryScrolling({ scrollPositionRestoration: 'top' }))
  ]
}).catch(error => console.error(error));
