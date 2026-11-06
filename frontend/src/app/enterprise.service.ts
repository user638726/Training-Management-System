import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Operations, AuditPage, Assignment, Approval, Certificate, Delivery, Department, ImportPreview, Integrations, ManagedAccount, ManagementReport, Material, Notification, Plan, Quiz, Schedule, Staff } from './enterprise.models';
@Injectable({ providedIn: 'root' })
export class EnterpriseService {
  private http = inject(HttpClient);
  private root = '/api/enterprise/';
  departments() { return this.http.get<Department[]>(this.root + 'departments'); }
  staff() { return this.http.get<Staff[]>(this.root + 'staff-directory'); }
  accounts() { return this.http.get<ManagedAccount[]>(this.root + 'accounts'); }
  plans() { return this.http.get<Plan[]>(this.root + 'plans'); }
  assignments() { return this.http.get<Assignment[]>(this.root + 'assignments'); }
  approvals() { return this.http.get<Approval[]>(this.root + 'approvals'); }
  certificates() { return this.http.get<Certificate[]>(this.root + 'certificates'); }
  notifications() { return this.http.get<Notification[]>(this.root + 'notifications'); }
  operations() { return this.http.get<Operations>(this.root + 'operations'); }
  integrations() { return this.http.get<Integrations>(this.root + 'integrations'); }
  materials(courseId: number) { return this.http.get<Material[]>(this.root + `courses/${courseId}/materials`); }
  quiz(courseId: number) { return this.http.get<Quiz>(this.root + `courses/${courseId}/quiz`); }
  reports(from: string, to: string, department: string) { return this.http.get<ManagementReport>(this.root + 'reports', { params: this.filters(from, to, department) }); }
  schedules() { return this.http.get<Schedule[]>(this.root + 'report-schedules'); }
  deliveries() { return this.http.get<Delivery[]>(this.root + 'report-deliveries'); }
  audit(page: number, entity: string) { return this.http.get<AuditPage>(this.root + 'audit', { params: { page, pageSize: 30, ...(entity ? { entity } : {}) } }); }
  post<T = unknown>(path: string, body: unknown = {}) { return this.http.post<T>(this.root + path, body); }
  put(path: string, body: unknown) { return this.http.put(this.root + path, body); }
  delete(path: string) { return this.http.delete(this.root + path); }
  preview(csv: string) { return this.post<ImportPreview>('imports/preview', { csv }); }
  hrPreview() { return this.post<ImportPreview>('imports/hr-preview'); }
  upload(courseId: number, file: File) { const form = new FormData(); form.append('file', file); return this.post(`courses/${courseId}/materials`, form); }
  download(path: string) { return this.http.get(this.root + path, { responseType: 'blob' }); }
  export(from: string, to: string, department: string) { return this.http.get(this.root + 'reports/export', { params: this.filters(from, to, department), responseType: 'blob' }); }
  private filters(from: string, to: string, department: string) {
    let p = new HttpParams();
    if (from) p = p.set('from', new Date(from + 'T00:00:00').toISOString());
    if (to) { const end = new Date(to + 'T00:00:00'); end.setDate(end.getDate() + 1); p = p.set('to', end.toISOString()); }
    return department ? p.set('department', department) : p;
  }
}
