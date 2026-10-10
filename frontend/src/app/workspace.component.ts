import { CommonModule } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { Component, DestroyRef, HostListener, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule, NgForm } from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { Observable, forkJoin, of } from 'rxjs';
import { ApiService } from './api.service';
import { AuthService, AuthUser } from './auth.service';
import { IconComponent } from './icon.component';
import { Course, CourseFormat, CourseInput, CourseStatus, Dashboard, Employee, EmployeeInput, Enrollment, EnrollmentStatus, Section } from './models';

type Modal = 'course' | 'employee' | 'enroll' | 'progress' | 'delete' | 'selfCancel' | null;

@Component({
  selector: 'app-workspace', standalone: true, imports: [CommonModule, FormsModule, RouterLink, IconComponent],
  templateUrl: './workspace.component.html'
})
export class WorkspaceComponent {
  readonly auth = inject(AuthService);
  private api = inject(ApiService);
  private destroyRef = inject(DestroyRef);
  section = inject(ActivatedRoute).snapshot.data['section'] as Section;
  today = new Date();
  title: Record<Section, string> = { overview: '團隊成長，一目瞭然。', courses: '課程管理', employees: '員工與帳號管理', enrollments: '報名與學習紀錄', reports: '訓練報表' };
  subtitle: Record<Section, string> = { overview: '掌握訓練進度，讓人才發展的每一步都有方向。', courses: '從課程規劃到開課執行，集中管理每一場學習。', employees: '管理團隊人才資料，讓學習資源找到對的人。', enrollments: '串起課程與員工，持續追蹤每一份學習成果。', reports: '將學習紀錄轉化為洞察，看見團隊成長的全貌。' };
  courseStatuses: CourseStatus[] = ['Draft', 'Published', 'Completed', 'Cancelled'];
  courseFormats: CourseFormat[] = ['InPerson', 'Online', 'Hybrid'];
  enrollmentStatuses: EnrollmentStatus[] = ['Registered', 'InProgress', 'Completed', 'Cancelled'];
  statusLabels: Record<string, string> = { Draft: '草稿', Published: '已發布', Completed: '已完成', Cancelled: '已取消', Registered: '已報名', InProgress: '學習中' };
  formatLabels: Record<CourseFormat, string> = { InPerson: '實體課程', Online: '線上課程', Hybrid: '混成課程' };
  courses = signal<Course[]>([]);
  employees = signal<Employee[]>([]);
  enrollments = signal<Enrollment[]>([]);
  accounts = signal<AuthUser[]>([]);
  selectedAccount: AuthUser | null = null;
  enrollingCourseId = signal<number | null>(null);
  dashboard = signal<Dashboard | null>(null);
  loading = signal(true);
  refreshing = signal(false);
  error = signal('');
  toast = signal('');
  modal = signal<Modal>(null);
  saving = signal(false);
  formError = signal('');
  editId: number | null = null;
  courseForm: CourseInput = this.newCourse();
  employeeForm: EmployeeInput = this.newEmployee();
  enrollmentForm: { courseId: number | null; employeeId: number | null } = { courseId: null, employeeId: null };
  progressForm: { status: EnrollmentStatus; progress: number; score: number | null } = { status: 'Registered', progress: 0, score: null };
  selectedEnrollment: Enrollment | null = null;
  cancelTarget: Enrollment | null = null;
  deleteTarget: { type: 'course' | 'employee'; id: number; name: string } | null = null;
  private focusBeforeModal: HTMLElement | null = null;
  private toastTimer?: ReturnType<typeof setTimeout>;
  private focusTimer?: ReturnType<typeof setTimeout>;
  search = signal('');
  accountSearch = signal('');
  categoryFilter = signal('');
  courseStatusFilter = signal('');
  departmentFilter = signal('');
  employeeStateFilter = signal('');
  enrollmentStatusFilter = signal('');
  enrollmentCourseFilter = signal('');
  reportDepartmentFilter = signal('');
  reportStatusFilter = signal('');

  categories = computed(() => [...new Set(this.courses().map(c => c.category))].sort((a, b) => a.localeCompare(b, 'zh-Hant')));
  departments = computed(() => [...new Set(this.employees().map(e => e.department))].sort((a, b) => a.localeCompare(b, 'zh-Hant')));
  filteredCourses = computed(() => this.courses().filter(course => {
    const query = this.search().trim().toLocaleLowerCase();
    return (!query || [course.title, course.description, course.instructor].some(value => value.toLocaleLowerCase().includes(query)))
      && (!this.categoryFilter() || course.category === this.categoryFilter()) && (!this.courseStatusFilter() || course.status === this.courseStatusFilter());
  }).sort((a, b) => new Date(b.startDate).getTime() - new Date(a.startDate).getTime()));
  filteredEmployees = computed(() => this.employees().filter(employee => {
    const query = this.search().trim().toLocaleLowerCase();
    return (!query || [employee.name, employee.employeeNumber, employee.email, employee.jobTitle].some(value => value.toLocaleLowerCase().includes(query)))
      && (!this.departmentFilter() || employee.department === this.departmentFilter())
      && (!this.employeeStateFilter() || employee.isActive === (this.employeeStateFilter() === 'active'));
  }));
  pendingAccounts = computed(() => this.accounts().filter(account => account.employeeId === null));
  myLinkedEmployee = computed(() => this.employees().find(employee => employee.id === this.auth.user()?.employeeId));
  filteredAccounts = computed(() => {
    const query = this.accountSearch().trim().toLocaleLowerCase();
    return this.accounts().filter(account => !query ||
      [account.username, account.displayName, this.linkedEmployeeFor(account)?.name ?? '',
        this.linkedEmployeeFor(account)?.employeeNumber ?? ''].some(value => value.toLocaleLowerCase().includes(query)))
      .sort((a, b) => Number(a.employeeId !== null) - Number(b.employeeId !== null) ||
        a.username.localeCompare(b.username, 'zh-Hant'));
  });
  filteredEnrollments = computed(() => this.enrollments().filter(enrollment => {
    const query = this.search().trim().toLocaleLowerCase();
    return (!query || [enrollment.employeeName, enrollment.employeeNumber, enrollment.courseTitle].some(value => value.toLocaleLowerCase().includes(query)))
      && (!this.departmentFilter() || enrollment.department === this.departmentFilter())
      && (!this.enrollmentStatusFilter() || enrollment.status === this.enrollmentStatusFilter())
      && (!this.enrollmentCourseFilter() || enrollment.courseId === Number(this.enrollmentCourseFilter()));
  }).sort((a, b) => new Date(b.registeredAt).getTime() - new Date(a.registeredAt).getTime()));
  reportRows = computed(() => this.enrollments().filter(e => (!this.reportDepartmentFilter() || e.department === this.reportDepartmentFilter()) && (!this.reportStatusFilter() || e.status === this.reportStatusFilter())));
  reportCompleted = computed(() => this.reportRows().filter(e => e.status === 'Completed').length);
  reportHours = computed(() => this.reportRows().filter(e => e.status === 'Completed').reduce((sum, e) => sum + (this.courses().find(c => c.id === e.courseId)?.durationHours ?? 0), 0));
  reportAverageScore = computed(() => {
    const scores = this.reportRows().filter(e => e.status === 'Completed' && e.score !== null).map(e => e.score!);
    return scores.length ? scores.reduce((sum, s) => sum + s, 0) / scores.length : null;
  });
  activeEmployees = computed(() => this.employees().filter(e => e.isActive));
  publishedCourses = computed(() => this.courses().filter(c => c.status === 'Published').sort((a, b) => new Date(a.startDate).getTime() - new Date(b.startDate).getTime()));
  availableEmployees = computed(() => {
    const courseId = this.enrollmentCourseSelection();
    return this.activeEmployees().filter(e => !this.enrollments().some(r => r.courseId === courseId && r.employeeId === e.id && r.status !== 'Cancelled'));
  });
  enrollmentCourseSelection = signal<number | null>(null);
  selectedCourse = computed(() => this.courses().find(c => c.id === this.enrollmentCourseSelection()));
  upcomingCourses = computed(() => this.publishedCourses().filter(c => new Date(c.endDate) >= this.today).slice(0, 3));
  pendingCount = computed(() => this.enrollments().filter(e => e.status === 'Registered' || e.status === 'InProgress').length);
  participatingEmployeeCount = computed(() => new Set(this.enrollments().filter(e => e.status !== 'Cancelled').map(e => e.employeeId)).size);
  monthlyMax = computed(() => Math.max(1, ...(this.dashboard()?.monthlyStats.map(m => Math.max(m.enrollments, m.completions)) ?? [])));

  constructor() {
    this.loadData();
    this.destroyRef.onDestroy(() => { clearTimeout(this.toastTimer); clearTimeout(this.focusTimer); });
  }

  loadData(refresh = false) {
    this.error.set('');
    if (refresh && this.dashboard()) this.refreshing.set(true); else this.loading.set(true);
    forkJoin({ courses: this.api.courses(), employees: this.api.employees(), enrollments: this.api.enrollments(),
      dashboard: this.api.dashboard(), accounts: this.auth.canManage() ? this.api.accounts() : of([] as AuthUser[]),
      profile: this.auth.canManage() ? of(null) : this.auth.refreshProfile() })
      .pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
        next: result => { this.courses.set(result.courses); this.employees.set(result.employees); this.enrollments.set(result.enrollments); this.dashboard.set(result.dashboard); this.accounts.set(result.accounts); this.loading.set(false); this.refreshing.set(false); },
        error: (error: HttpErrorResponse) => { this.error.set(this.errorMessage(error)); this.loading.set(false); this.refreshing.set(false); }
      });
  }

  statusClass(status: string) { return ({ Draft: 'neutral', Published: 'green', Completed: 'green', Cancelled: 'red', Registered: 'blue', InProgress: 'amber' } as Record<string, string>)[status] || 'neutral'; }
  courseTone(category: string) { const tones = ['mint', 'sand', 'lilac', 'sky']; return tones[[...category].reduce((sum, char) => sum + char.charCodeAt(0), 0) % tones.length]; }
  initial(name: string) { return name.slice(-2); }
  departmentRate(department: { completedCount: number; enrollmentCount: number }) { return department.enrollmentCount ? Math.round(department.completedCount / department.enrollmentCount * 100) : 0; }
  courseFill(course: Course) { return Math.min(100, Math.round(course.enrolledCount / course.capacity * 100)); }
  monthName(month: string) { return `${Number(month.split('-')[1])} 月`; }
  hoursFor(courseId: number) { return this.courses().find(c => c.id === courseId)?.durationHours ?? 0; }
  employeeEnrollmentCount(employeeId: number) { return this.enrollments().filter(e => e.employeeId === employeeId && e.status !== 'Cancelled').length; }
  employeeCompletedCount(employeeId: number) { return this.enrollments().filter(e => e.employeeId === employeeId && e.status === 'Completed').length; }
  linkedAccount(employeeId: number) { return this.accounts().find(account => account.employeeId === employeeId); }
  linkedEmployeeFor(account: AuthUser) { return this.employees().find(employee => employee.id === account.employeeId); }
  myEnrollment(courseId: number) { return this.enrollments().find(e => e.courseId === courseId && e.employeeId === this.auth.user()?.employeeId && e.status !== 'Cancelled'); }
  dateOnly(value: string) { return new Intl.DateTimeFormat('zh-TW', { year: 'numeric', month: '2-digit', day: '2-digit' }).format(new Date(value)); }

  private newCourse(): CourseInput {
    const start = new Date(); start.setDate(start.getDate() + 7); start.setHours(9, 0, 0, 0);
    const end = new Date(start); end.setHours(12);
    return { title: '', description: '', category: '', instructor: '', location: '', format: 'InPerson', startDate: this.localDateInput(start), endDate: this.localDateInput(end), durationHours: 3, capacity: 30, status: 'Draft' };
  }
  private newEmployee(): EmployeeInput { return { employeeNumber: '', name: '', email: '', department: '', jobTitle: '', isActive: true }; }
  private localDateInput(date: Date) { const pad = (n: number) => String(n).padStart(2, '0'); return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}T${pad(date.getHours())}:${pad(date.getMinutes())}`; }

  openCourse(course?: Course) {
    this.editId = course?.id ?? null;
    this.courseForm = course ? { title: course.title, description: course.description, category: course.category, instructor: course.instructor, location: course.location, format: course.format, startDate: this.localDateInput(new Date(course.startDate)), endDate: this.localDateInput(new Date(course.endDate)), durationHours: course.durationHours, capacity: course.capacity, status: course.status } : this.newCourse();
    this.openModal('course');
  }
  openEmployee(employee?: Employee) { this.selectedAccount = null; this.editId = employee?.id ?? null; this.employeeForm = employee ? { employeeNumber: employee.employeeNumber, name: employee.name, email: employee.email, department: employee.department, jobTitle: employee.jobTitle, isActive: employee.isActive } : this.newEmployee(); this.openModal('employee'); }
  openEmployeeForAccount(account: AuthUser) {
    if (account.employeeId !== null) return;
    this.selectedAccount = account;
    this.editId = null;
    this.employeeForm = { ...this.newEmployee(), name: account.displayName };
    this.openModal('employee');
  }
  selfEnroll(course: Course) {
    if (this.auth.canManage() || this.enrollingCourseId() !== null || !this.auth.user()?.employeeId || this.myEnrollment(course.id)) return;
    this.enrollingCourseId.set(course.id);
    this.api.selfEnroll(course.id).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: () => { this.enrollingCourseId.set(null); this.showToast(`已成功報名「${course.title}」。`); this.loadData(true); },
      error: (error: HttpErrorResponse) => { this.enrollingCourseId.set(null); this.showToast(this.errorMessage(error)); this.loadData(true); }
    });
  }
  openSelfCancel(enrollment: Enrollment) {
    if (this.auth.canManage() || enrollment.employeeId !== this.auth.user()?.employeeId ||
      !['Registered', 'InProgress'].includes(enrollment.status)) return;
    this.cancelTarget = enrollment;
    this.openModal('selfCancel');
  }
  confirmSelfCancel() {
    const enrollment = this.cancelTarget;
    if (!enrollment || this.saving() || enrollment.employeeId !== this.auth.user()?.employeeId) return;
    this.saving.set(true); this.formError.set('');
    this.api.cancelOwnEnrollment(enrollment.id).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: () => { this.saving.set(false); this.closeModal(); this.showToast(`已取消「${enrollment.courseTitle}」的報名。`); this.loadData(true); },
      error: (error: HttpErrorResponse) => { this.saving.set(false); this.formError.set(this.errorMessage(error)); this.loadData(true); }
    });
  }
  openEnrollment(course?: Course) { this.enrollmentForm = { courseId: course?.id ?? null, employeeId: null }; this.enrollmentCourseSelection.set(course?.id ?? null); this.openModal('enroll'); }
  enrollmentCourseChanged(courseId: number | null) { this.enrollmentForm.courseId = courseId; this.enrollmentCourseSelection.set(courseId); this.enrollmentForm.employeeId = null; }
  openProgress(enrollment: Enrollment) { this.selectedEnrollment = enrollment; this.progressForm = { status: enrollment.status, progress: enrollment.progress, score: enrollment.score }; this.openModal('progress'); }
  progressStatusChanged(status: EnrollmentStatus) { this.progressForm.status = status; if (status === 'Completed') this.progressForm.progress = 100; if (status === 'Registered') this.progressForm.progress = 0; }
  openDelete(type: 'course' | 'employee', id: number, name: string) { this.deleteTarget = { type, id, name }; this.openModal('delete'); }
  private openModal(modal: Modal) { if (!this.auth.canManage() && modal !== 'selfCancel') return; this.focusBeforeModal = document.activeElement as HTMLElement; this.formError.set(''); this.modal.set(modal); this.focusTimer = setTimeout(() => document.querySelector<HTMLElement>('.modal [data-autofocus]')?.focus(), 0); }
  closeModal() { if (this.saving()) return; this.modal.set(null); this.cancelTarget = null; this.selectedAccount = null; this.formError.set(''); this.focusBeforeModal?.focus(); }
  @HostListener('document:keydown', ['$event']) onKeydown(event: KeyboardEvent) {
    if (!this.modal()) return;
    if (event.key === 'Escape') { event.preventDefault(); this.closeModal(); }
    if (event.key === 'Tab') {
      const elements = Array.from(document.querySelectorAll<HTMLElement>('.modal button:not([disabled]), .modal input:not([disabled]), .modal select:not([disabled]), .modal textarea:not([disabled]), .modal a[href]')).filter(e => e.offsetParent !== null);
      const first = elements[0]; const last = elements[elements.length - 1];
      if (event.shiftKey && document.activeElement === first) { last?.focus(); event.preventDefault(); }
      if (!event.shiftKey && document.activeElement === last) { first?.focus(); event.preventDefault(); }
    }
  }

  saveCourse(form: NgForm) {
    if (!this.canManageAction()) return;
    if (!form.valid) { form.control.markAllAsTouched(); this.formError.set('請確認必填欄位與輸入格式。'); return; }
    const input = this.courseForm;
    if (!Number.isInteger(Number(input.capacity))) { this.formError.set('課程名額必須為整數。'); return; }
    if (new Date(input.endDate) <= new Date(input.startDate)) { this.formError.set('課程結束時間必須晚於開始時間。'); return; }
    if (![input.title, input.category, input.instructor, input.location].every(v => v.trim())) { this.formError.set('課程名稱、分類、講師與地點不可只包含空白。'); return; }
    const payload: CourseInput = { ...input, title: input.title.trim(), description: input.description.trim(), category: input.category.trim(), instructor: input.instructor.trim(), location: input.location.trim(), startDate: new Date(input.startDate).toISOString(), endDate: new Date(input.endDate).toISOString(), durationHours: Number(input.durationHours), capacity: Number(input.capacity) };
    this.save(this.api.saveCourse(payload, this.editId), this.editId === null ? '課程已建立。' : '課程資料已更新。');
  }
  saveEmployee(form: NgForm) {
    if (!this.canManageAction()) return;
    if (!form.valid) { form.control.markAllAsTouched(); this.formError.set('請確認必填欄位與電子郵件格式。'); return; }
    const input = this.employeeForm;
    if (![input.employeeNumber, input.name, input.department, input.jobTitle].every(v => v.trim())) { this.formError.set('員工編號、姓名、部門與職稱不可只包含空白。'); return; }
    const payload = { ...input, employeeNumber: input.employeeNumber.trim(), name: input.name.trim(), email: input.email.trim(), department: input.department.trim(), jobTitle: input.jobTitle.trim() };
    this.save(this.selectedAccount ? this.api.createEmployeeForAccount(Number(this.selectedAccount.id), payload) : this.api.saveEmployee(payload, this.editId),
      this.selectedAccount ? '新員工已建立，帳號現在可以自行報名課程。' : this.editId === null ? '員工已新增。' : '員工資料已更新。');
  }
  saveEnrollment(form: NgForm) {
    if (!this.canManageAction()) return;
    if (!form.valid || this.enrollmentForm.courseId === null || this.enrollmentForm.employeeId === null) { form.control.markAllAsTouched(); this.formError.set('請選擇課程與員工。'); return; }
    this.save(this.api.enroll(this.enrollmentForm.courseId, this.enrollmentForm.employeeId), '報名成功，已建立學習紀錄。');
  }
  saveProgress(form: NgForm) {
    if (!this.canManageAction()) return;
    if (!form.valid || !this.selectedEnrollment) { form.control.markAllAsTouched(); this.formError.set('進度與分數需介於 0 到 100。'); return; }
    if (!Number.isInteger(Number(this.progressForm.progress))) { this.formError.set('學習進度必須為 0 到 100 的整數百分比。'); return; }
    this.save(this.api.updateEnrollment(this.selectedEnrollment.id, this.progressForm.status, Number(this.progressForm.progress), this.progressForm.score === null || this.progressForm.score === undefined ? null : Number(this.progressForm.score)), '學習紀錄已更新。');
  }
  confirmDelete() {
    if (!this.canManageAction()) return;
    const target = this.deleteTarget; if (!target) return;
    this.save(target.type === 'course' ? this.api.deleteCourse(target.id) : this.api.deleteEmployee(target.id), `${target.type === 'course' ? '課程' : '員工'}已刪除。`);
  }
  private save(request: Observable<unknown>, success: string) {
    if (!this.canManageAction()) return;
    if (this.saving()) return;
    this.saving.set(true); this.formError.set('');
    request.pipe(takeUntilDestroyed(this.destroyRef)).subscribe({ next: () => { this.saving.set(false); this.closeModal(); this.showToast(success); this.loadData(true); }, error: (error: HttpErrorResponse) => { this.saving.set(false); this.formError.set(this.errorMessage(error)); } });
  }
  private errorMessage(error: HttpErrorResponse) {
    if (error.status === 0) return '無法連線至訓練管理 API，請確認後端已啟動，再重試一次。';
    if (error.error && typeof error.error === 'object') {
      const validation = error.error.errors && Object.values(error.error.errors).flat().filter(v => typeof v === 'string');
      if (validation?.length) return validation.join(' ');
      return error.error.detail || error.error.title || '無法完成操作，請稍後重試。';
    }
    return error.status === 404 ? '找不到這筆資料，請重新整理後再操作。' : '無法完成操作，請稍後重試。';
  }
  private canManageAction() {
    if (this.auth.canManage()) return true;
    this.formError.set('此帳號可檢視訓練資料及匯出報表，管理操作需由系統管理員執行。');
    return false;
  }
  private showToast(message: string) { clearTimeout(this.toastTimer); this.toast.set(message); this.toastTimer = setTimeout(() => this.toast.set(''), 4500); }
  resetFilters() { this.search.set(''); this.categoryFilter.set(''); this.courseStatusFilter.set(''); this.departmentFilter.set(''); this.employeeStateFilter.set(''); this.enrollmentStatusFilter.set(''); this.enrollmentCourseFilter.set(''); this.reportDepartmentFilter.set(''); this.reportStatusFilter.set(''); }

  exportReport() {
    if (!this.reportRows().length) return;
    const rows: (string | number)[][] = [['員工編號', '員工姓名', '部門', '課程名稱', '狀態', '學習進度 (%)', '測驗分數', '課程時數', '報名日期', '完訓日期'], ...this.reportRows().map(e => [e.employeeNumber, e.employeeName, e.department, e.courseTitle, this.statusLabels[e.status], e.progress, e.score ?? '', this.hoursFor(e.courseId), this.dateOnly(e.registeredAt), e.completedAt ? this.dateOnly(e.completedAt) : ''])];
    const escape = (value: string | number) => { let text = String(value); if (/^[\s]*[=+\-@]/.test(text) || /^[\t\r\n]/.test(text)) text = `'${text}`; return `"${text.replace(/"/g, '""')}"`; };
    const blob = new Blob(['\uFEFF' + rows.map(row => row.map(escape).join(',')).join('\r\n')], { type: 'text/csv;charset=utf-8;' });
    const url = URL.createObjectURL(blob); const link = document.createElement('a'); link.href = url; link.download = `育成_訓練報表_${this.localDateInput(this.today).slice(0, 10)}.csv`; document.body.appendChild(link); link.click(); link.remove(); setTimeout(() => URL.revokeObjectURL(url), 0); this.showToast(`已匯出 ${this.reportRows().length} 筆訓練紀錄。`);
  }
}
