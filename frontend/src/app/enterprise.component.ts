import { Component, DestroyRef, computed, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { HttpErrorResponse } from '@angular/common/http';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Observable, finalize, forkJoin, of } from 'rxjs';
import { ApiService } from './api.service';
import { AuthService } from './auth.service';
import { EnterpriseService } from './enterprise.service';
import { Operations, Approval, Assignment, AuditPage, Certificate, Delivery, Department, EnterpriseSection, ImportPreview, Integrations, ManagedAccount, ManagementReport, Material, Notification, Plan, Question, Quiz, Schedule, Staff } from './enterprise.models';
import { Course, Employee, Enrollment } from './models';

@Component({ selector: 'app-enterprise', standalone: true, imports: [CommonModule, FormsModule, RouterLink], templateUrl: './enterprise.component.html', styleUrl: './enterprise.component.css' })
export class EnterpriseComponent {
  auth = inject(AuthService); private api = inject(ApiService); apiEnterprise = inject(EnterpriseService);
  private destroy = inject(DestroyRef); private router = inject(Router);
  section = inject(ActivatedRoute).snapshot.data['enterpriseSection'] as EnterpriseSection;
  titles: Record<EnterpriseSection, string> = { organization: '組織與部門權限', accounts: '帳號與邀請管理', plans: '必修與學習計畫', learning: '我的學習中心', approvals: '報名簽核', materials: '教材、測驗與出席', notifications: '通知中心', 'management-reports': '主管決策報表', audit: '操作稽核', imports: '員工批次匯入', integrations: '企業整合與維運', settings: '帳號安全設定' };
  operations = signal<Operations | null>(null);
  loading = signal(false); busy = signal(false); error = signal(''); message = signal('');
  departments = signal<Department[]>([]); staff = signal<Staff[]>([]); accounts = signal<ManagedAccount[]>([]);
  courses = signal<Course[]>([]); employees = signal<Employee[]>([]); enrollments = signal<Enrollment[]>([]);
  plans = signal<Plan[]>([]); assignments = signal<Assignment[]>([]); approvals = signal<Approval[]>([]);
  certificates = signal<Certificate[]>([]); notifications = signal<Notification[]>([]); materials = signal<Material[]>([]);
  quiz = signal<Quiz | null>(null); report = signal<ManagementReport | null>(null); schedules = signal<Schedule[]>([]); deliveries = signal<Delivery[]>([]);
  audit = signal<AuditPage | null>(null); preview = signal<ImportPreview | null>(null); integrations = signal<Integrations | null>(null);
  roles = ['Admin', 'HR', 'Manager', 'Instructor', 'Employee']; roleLabels: Record<string, string> = { Admin: '系統管理員', HR: '人資', Manager: '部門主管', Instructor: '講師', Employee: '員工' };
  labels: Record<string, string> = { Pending: '待簽核', Approved: '已核准', Rejected: '已退回', Withdrawn: '已撤回', Registered: '已報名', InProgress: '學習中', Completed: '已完訓', Cancelled: '已取消', InApp: '站內通知', Sent: '郵件已寄送', Retry: '郵件重試中', Failed: '郵件寄送失敗', NoAddress: '無郵件地址' };
  departmentForm = { id: 0, name: '', parentId: null as number | null, managerUserId: null as number | null, rowVersion: '' };
  accountForm: ManagedAccount | null = null;
  invite = { email: '', role: 'Employee', employeeId: null as number | null }; inviteLink = signal('');
  planForm = this.newPlan(); selectedCourseId = 0; selectedEnrollmentId = 0;
  courseSettings = { instructorUserId: null as number | null, requiresApproval: false, requiresAttendance: false, requiresQuiz: false, passScore: 60, cost: 0, prerequisiteCourseId: null as number | null, certificateValidityMonths: null as number | null, rowVersion: '' };
  questions: Question[] = []; answers: Record<number, number> = {}; decisionComments: Record<number, string> = {};
  survey = { rating: 5, feedback: '' }; reportFrom = ''; reportTo = ''; reportDepartment = '';
  schedule = { title: '每週訓練追蹤', department: null as string | null, frequency: 'Weekly' };
  csv = ''; auditPage = 1; auditEntity = ''; currentPassword = ''; newPassword = '';
  ownCertificates = computed(() => this.certificates().filter(x => x.employeeId === this.auth.user()?.employeeId));
  visibleAssignments = () => this.section === 'learning' ? this.assignments().filter(x => x.employeeId === this.auth.user()?.employeeId) : this.assignments();
  ownEnrollments = computed(() => this.enrollments().filter(x => x.employeeId === this.auth.user()?.employeeId));
  courseEnrollments = () => this.enrollments().filter(x => x.courseId === this.selectedCourseId);
  managers = computed(() => this.staff().filter(x => x.role === 'Manager'));
  instructors = computed(() => this.staff().filter(x => x.role === 'Instructor'));
  canReview = computed(() => ['Admin', 'HR', 'Manager'].includes(this.auth.user()?.role || ''));
  canTeach = () => this.auth.canManage() || this.courses().some(x => x.id === this.selectedCourseId && x.instructorUserId === Number(this.auth.user()?.id));
  allowed = computed(() => {
    const role = this.auth.user()?.role;
    if (['accounts', 'audit', 'integrations'].includes(this.section)) return role === 'Admin';
    if (['organization', 'plans', 'imports'].includes(this.section)) return this.auth.canManage();
    if (this.section === 'materials') return this.auth.canManage() || role === 'Instructor';
    return true;
  });
  constructor() { if (this.allowed()) this.load(); }
  private newPlan(): Plan { return { id: 0, title: '', department: null, jobTitle: null, newHireDays: null, dueDays: 30, recurrenceMonths: 0, isActive: true, courseIds: [] }; }
  load() {
    this.error.set(''); this.loading.set(true);
    const base: Record<string, Observable<unknown>> = {};
    if (['organization', 'accounts', 'plans', 'learning', 'materials', 'management-reports'].includes(this.section)) base['departments'] = this.apiEnterprise.departments();
    if (['plans', 'learning', 'materials'].includes(this.section)) base['courses'] = this.api.courses();
    if (['accounts', 'learning', 'materials'].includes(this.section)) base['employees'] = this.api.employees();
    if (['learning', 'materials'].includes(this.section)) base['enrollments'] = this.api.enrollments();
    if (['organization', 'materials'].includes(this.section) && this.auth.canManage()) base['staff'] = this.apiEnterprise.staff();
    if (this.section === 'accounts') base['accounts'] = this.apiEnterprise.accounts();
    if (this.section === 'plans') base['plans'] = this.apiEnterprise.plans();
    if (['plans', 'learning'].includes(this.section)) base['assignments'] = this.apiEnterprise.assignments();
    if (this.section === 'learning') base['certificates'] = this.apiEnterprise.certificates();
    if (['approvals', 'learning'].includes(this.section)) base['approvals'] = this.apiEnterprise.approvals();
    if (this.section === 'notifications') base['notifications'] = this.apiEnterprise.notifications();
    if (this.section === 'integrations') { base['integrations'] = this.apiEnterprise.integrations(); base['operations'] = this.apiEnterprise.operations(); }
    if (this.section === 'audit') base['audit'] = this.apiEnterprise.audit(this.auditPage, this.auditEntity);
    if (this.section === 'management-reports') {
      base['report'] = this.apiEnterprise.reports(this.reportFrom, this.reportTo, this.reportDepartment);
      if (this.canReview()) { base['schedules'] = this.apiEnterprise.schedules(); base['deliveries'] = this.apiEnterprise.deliveries(); }
    }
    (Object.keys(base).length ? forkJoin(base) : of({})).pipe(takeUntilDestroyed(this.destroy), finalize(() => this.loading.set(false))).subscribe({
      next: values => {
        // Each key is assigned to its matching typed state after the request succeeds.
        const v = values as { operations?: Operations; departments?: Department[]; staff?: Staff[]; accounts?: ManagedAccount[]; courses?: Course[]; employees?: Employee[]; enrollments?: Enrollment[]; plans?: Plan[]; assignments?: Assignment[]; approvals?: Approval[]; certificates?: Certificate[]; notifications?: Notification[]; integrations?: Integrations; audit?: AuditPage; report?: ManagementReport; schedules?: Schedule[]; deliveries?: Delivery[] };
        if (v.operations) this.operations.set(v.operations);
        if (v.departments) this.departments.set(v.departments); if (v.staff) this.staff.set(v.staff); if (v.accounts) this.accounts.set(v.accounts);
        if (v.courses) this.courses.set(v.courses); if (v.employees) this.employees.set(v.employees); if (v.enrollments) this.enrollments.set(v.enrollments);
        if (v.plans) this.plans.set(v.plans); if (v.assignments) this.assignments.set(v.assignments); if (v.approvals) this.approvals.set(v.approvals);
        if (v.certificates) this.certificates.set(v.certificates); if (v.notifications) this.notifications.set(v.notifications); if (v.integrations) this.integrations.set(v.integrations);
        if (v.audit) this.audit.set(v.audit); if (v.report) this.report.set(v.report); if (v.schedules) this.schedules.set(v.schedules); if (v.deliveries) this.deliveries.set(v.deliveries);
        if (this.selectedCourseId) this.loadCourse();
      }, error: e => this.fail(e)
    });
  }
  act<T>(request: Observable<T>, success: string, callback?: (value: T) => void) {
    if (this.busy()) return; this.busy.set(true); this.error.set(''); this.message.set('');
    request.pipe(takeUntilDestroyed(this.destroy), finalize(() => this.busy.set(false))).subscribe({ next: v => { this.message.set(success); if (callback) callback(v); else this.load(); }, error: e => this.fail(e) });
  }
  private fail(e: HttpErrorResponse) { this.error.set(e.error?.errors ? Object.values(e.error.errors).flat().join(' ') : e.error?.title || (e.status === 0 ? '無法連線，請稍後重試。' : '無法完成操作。')); }
  editDepartment(d: Department) { this.departmentForm = { ...d }; }
  saveDepartment() { const r = this.departmentForm; this.act(r.id ? this.apiEnterprise.put(`departments/${r.id}`, r) : this.apiEnterprise.post('departments', r), '部門已儲存。', () => { this.departmentForm = { id: 0, name: '', parentId: null, managerUserId: null, rowVersion: '' }; this.load(); }); }
  editAccount(account: ManagedAccount) { this.accountForm = { ...account }; }
  hasEnrollment(courseId: number) { return this.ownEnrollments().some(x => x.courseId === courseId && x.status !== 'Cancelled'); }
  selectLearningCourse(courseId: number) { this.selectedCourseId = courseId; this.loadCourse(); }
  saveAccount() { if (!this.accountForm) return; this.act(this.apiEnterprise.put(`accounts/${this.accountForm.id}`, this.accountForm), '帳號已更新；該帳號需重新登入。', () => { this.accountForm = null; this.load(); }); }
  sendInvite() { this.act(this.apiEnterprise.post<{ registrationPath: string }>('invitations', this.invite), '邀請已建立，請將連結交給受邀者。', v => { this.inviteLink.set(location.origin + v.registrationPath); }); }
  editPlan(p: Plan) { this.planForm = { ...p, courseIds: [...p.courseIds] }; }
  resetPlan() { this.planForm = this.newPlan(); }
  toggleCourse(id: number, checked: boolean) { this.planForm.courseIds = checked ? [...this.planForm.courseIds, id] : this.planForm.courseIds.filter(x => x !== id); }
  savePlan() { const r = this.planForm; this.act(r.id ? this.apiEnterprise.put(`plans/${r.id}`, r) : this.apiEnterprise.post('plans', r), '學習計畫已儲存，系統會自動指派符合條件的員工。', () => { this.resetPlan(); this.load(); }); }
  assign(p: Plan) { this.act(this.apiEnterprise.post<{ assigned: number }>(`plans/${p.id}/assign`), '指派完成。', v => { this.message.set(`已新增 ${v.assigned} 筆任務；既有任務不重複建立。`); this.load(); }); }
  decide(a: Approval, approve: boolean) { this.act(this.apiEnterprise.post(`approvals/${a.id}/decision`, { approve, comment: this.decisionComments[a.id] || '', rowVersion: a.rowVersion }), approve ? '申請已核准，名額已確認。' : '申請已退回。'); }
  withdraw(a: Approval) { this.act(this.apiEnterprise.post(`approvals/${a.id}/withdraw`), '申請已撤回。'); }
  isOwn(employeeId: number) { return employeeId === this.auth.user()?.employeeId; }
  isOverdue(date: string) { return new Date(date).getTime() < Date.now(); }
  enroll(course: Course) {
    this.act(course.requiresApproval ? this.apiEnterprise.post('approvals', { courseId: course.id }) : this.api.selfEnroll(course.id), course.requiresApproval ? '報名申請已送出。' : '報名已確認。');
  }
  loadCourse() {
    const c = this.courses().find(x => x.id === Number(this.selectedCourseId)); this.selectedCourseId = Number(this.selectedCourseId);
    if (!c) { this.quiz.set(null); this.materials.set([]); return; }
    this.courseSettings = { instructorUserId: c.instructorUserId ?? null, requiresApproval: c.requiresApproval ?? false, requiresAttendance: c.requiresAttendance ?? false,
      requiresQuiz: c.requiresQuiz ?? false, passScore: c.passScore ?? 60, cost: c.cost ?? 0, prerequisiteCourseId: c.prerequisiteCourseId ?? null,
      certificateValidityMonths: c.certificateValidityMonths ?? null, rowVersion: c.rowVersion ?? '' };
    forkJoin({ materials: this.apiEnterprise.materials(c.id), quiz: this.apiEnterprise.quiz(c.id) }).pipe(takeUntilDestroyed(this.destroy)).subscribe({
      next: v => { this.materials.set(v.materials); this.quiz.set(v.quiz); this.questions = v.quiz.questions.map(x => ({ ...x, options: [...x.options] })); this.answers = {}; }, error: e => this.fail(e)
    });
  }
  saveSettings() { this.act(this.apiEnterprise.put(`courses/${this.selectedCourseId}/settings`, this.courseSettings), '課程條件已更新。'); }
  fileSelected(event: Event, kind: 'material' | 'csv') {
    const input = event.target as HTMLInputElement; const file = input.files?.[0]; if (!file) return;
    if (kind === 'material') this.act(this.apiEnterprise.upload(this.selectedCourseId, file), '教材已上傳。');
    else if (file.size > 2_000_000) this.error.set('CSV 不可超過 2 MB。');
    else { void file.text().then(text => { this.csv = text; this.preview.set(null); }).catch(() => this.error.set('無法讀取檔案。')); }
    input.value = '';
  }
  addQuestion() { this.questions.push({ id: 0, prompt: '', options: ['', ''], correctOption: 0 }); }
  saveQuiz() { const course = this.courses().find(x => x.id === this.selectedCourseId); this.act(this.apiEnterprise.put(`courses/${this.selectedCourseId}/quiz`, { rowVersion: course?.rowVersion, questions: this.questions }), '測驗已發布為新版本。'); }
  attendance(e: Enrollment, attended: boolean) { this.act(this.apiEnterprise.post(`enrollments/${e.id}/attendance`, { attended, rowVersion: e.rowVersion }), '出席紀錄已更新。'); }
  submitQuiz() { const e = this.ownEnrollments().find(x => x.courseId === this.selectedCourseId && x.status !== 'Cancelled'); if (!e || !this.quiz()) return;
    this.act(this.apiEnterprise.post<{ score: number; passed: boolean }>(`enrollments/${e.id}/quiz`, { answers: this.answers, revision: this.quiz()!.revision }), '測驗已提交。', v => { this.message.set(`測驗分數 ${v.score} 分，${v.passed ? '已通過；若有出席要求，確認後即完訓。' : '尚未及格，可再次作答。'}`); this.load(); }); }
  submitSurvey() { this.act(this.apiEnterprise.post(`enrollments/${this.selectedEnrollmentId}/survey`, this.survey), '感謝回饋，問卷已送出。'); }
  markRead(n: Notification) { this.act(this.apiEnterprise.post(`notifications/${n.id}/read`), '已標示為已讀。'); }
  refreshReport() { this.load(); }
  saveSchedule() { this.act(this.apiEnterprise.post('report-schedules', this.schedule), '報表排程已建立。'); }
  stopSchedule(s: Schedule) { this.act(this.apiEnterprise.delete(`report-schedules/${s.id}`), '排程已停用。'); }
  exportReport() { this.act(this.apiEnterprise.export(this.reportFrom, this.reportTo, this.reportDepartment), '報表已下載。', blob => this.saveBlob(blob, 'training-report.csv')); }
  download(path: string, name: string) { this.act(this.apiEnterprise.download(path), '檔案已下載。', blob => this.saveBlob(blob, name)); }
  private saveBlob(blob: Blob, name: string) { const url = URL.createObjectURL(blob); const link = document.createElement('a'); link.href = url; link.download = name; link.click(); setTimeout(() => URL.revokeObjectURL(url), 1000); }
  previewCsv() { this.act(this.apiEnterprise.preview(this.csv), '預覽完成，請確認新增、更新與錯誤。', v => this.preview.set(v)); }
  previewHr() { this.act(this.apiEnterprise.hrPreview(), '人事資料已取得，請確認後再匯入。', v => this.preview.set(v)); }
  commitImport() { const p = this.preview(); if (!p?.valid) return; this.act(this.apiEnterprise.post(`imports/${p.id}/commit`), '員工資料已匯入。', () => { this.preview.set(null); this.csv = ''; }); }
  template() { this.saveBlob(new Blob(['\uFEFFemployeeNumber,name,email,department,jobTitle,isActive,hiredAt\r\nEMP-001,王小明,example@company.com,人資部,專員,true,2026-11-01\r\n'], { type: 'text/csv;charset=utf-8' }), 'employee-import-template.csv'); }
  auditNext(delta: number) { this.auditPage += delta; this.load(); }
  changePassword() { this.act(this.apiEnterprise.post('password', { currentPassword: this.currentPassword, newPassword: this.newPassword }), '密碼已更新，請重新登入。', () => { this.currentPassword = ''; this.newPassword = ''; this.auth.logout().subscribe(() => void this.router.navigate(['/login'])); }); }
  runJobs() { this.act(this.apiEnterprise.post('operations/run'), '指派、提醒與報表排程已執行。'); }
  departmentName(id: number | null) { return this.departments().find(x => x.id === id)?.name || '—'; }
  staffName(id: number | null) { return this.staff().find(x => x.id === id)?.displayName || '—'; }
}
