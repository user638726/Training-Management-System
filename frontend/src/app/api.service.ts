import { map } from 'rxjs';
import { inject, Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { AuthUser } from './auth.service';
import { Course, CourseInput, Dashboard, Employee, EmployeeInput, Enrollment, EnrollmentStatus } from './models';

@Injectable({ providedIn: 'root' })
export class ApiService {
  private http = inject(HttpClient);
  page<T>(resource: string, params: Record<string, string>) { return this.http.get<T[]>('/api/' + resource, { params, observe: 'response' }).pipe(map(r => ({ items: r.body || [], total: Number(r.headers.get('X-Total-Count') || 0) }))); }
  facets() { return this.http.get<{ categories: string[]; departments: string[] }>('/api/enterprise/facets'); }
  exportReport(department: string, status: string) { return this.http.get('/api/enterprise/reports/export', { params: { department, status }, responseType: 'blob' }); }
  courses() { return this.http.get<Course[]>('/api/courses'); }
  employees() { return this.http.get<Employee[]>('/api/employees'); }
  enrollments() { return this.http.get<Enrollment[]>('/api/enrollments'); }
  dashboard() { return this.http.get<Dashboard>('/api/dashboard'); }
  accounts() { return this.http.get<AuthUser[]>('/api/accounts'); }
  createEmployeeForAccount(accountId: number, input: EmployeeInput) { return this.http.post<Employee>(`/api/accounts/${accountId}/employee`, input); }
  requestApproval(courseId: number) { return this.http.post('/api/enterprise/approvals', { courseId }); }
  selfEnroll(courseId: number) { return this.http.post<Enrollment>('/api/enrollments/self', { courseId }); }
  cancelOwnEnrollment(id: number) { return this.http.post<Enrollment>(`/api/enrollments/${id}/cancel`, {}); }
  saveCourse(input: CourseInput, id: number | null) { return id === null ? this.http.post<Course>('/api/courses', input) : this.http.put<Course>(`/api/courses/${id}`, input); }
  saveEmployee(input: EmployeeInput, id: number | null) { return id === null ? this.http.post<Employee>('/api/employees', input) : this.http.put<Employee>(`/api/employees/${id}`, input); }
  enroll(courseId: number, employeeId: number) { return this.http.post<Enrollment>('/api/enrollments', { courseId, employeeId }); }
  updateEnrollment(id: number, status: EnrollmentStatus, progress: number, score: number | null, rowVersion?: string) { return this.http.put<Enrollment>(`/api/enrollments/${id}`, { status, progress, score, rowVersion }); }
  deleteCourse(id: number) { return this.http.delete(`/api/courses/${id}`); }
  deleteEmployee(id: number) { return this.http.delete(`/api/employees/${id}`); }
}
