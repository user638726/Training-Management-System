import { EmployeeInput } from './models';
export interface Department { id: number; name: string; parentId: number | null; managerUserId: number | null; rowVersion: string; }
export interface ManagedAccount { id: number; username: string; displayName: string; role: string; isActive: boolean; isApproved: boolean; employeeId: number | null; externalSubject: string | null; rowVersion: string; }
export interface Staff { id: number; displayName: string; role: string; }
export interface Plan { id: number; title: string; department: string | null; jobTitle: string | null; newHireDays: number | null; dueDays: number; recurrenceMonths: number; isActive: boolean; courseIds: number[]; rowVersion?: string; }
export interface Assignment { id: number; employeeId: number; employeeName: string; department: string; courseId: number; courseTitle: string; planTitle: string; cycle: string; assignedAt: string; dueAt: string; completedAt: string | null; }
export interface Approval { id: number; courseId: number; courseTitle: string; employeeId: number; employeeName: string; department: string; status: string; requestedAt: string; reviewedAt: string | null; comment: string; rowVersion: string; }
export interface Material { id: number; fileName: string; contentType: string; uploadedAt: string; }
export interface Question { id: number; prompt: string; options: string[]; correctOption: number | null; }
export interface Quiz { revision: number; passScore: number; questions: Question[]; }
export interface Certificate { id: number; employeeId: number; serial: string; employeeName: string; courseTitle: string; hours: number; issuedAt: string; expiresAt: string | null; revokedAt: string | null; }
export interface Notification { id: number; title: string; body: string; link: string; createdAt: string; readAt: string | null; deliveryStatus: string; }
export interface ManagementReport { enrolled: number; completed: number; hours: number; cost: number; satisfaction: number | null; departments: { department: string; assigned: number; completed: number; overdue: number }[]; overdue: { id: number; employeeName: string; department: string; courseTitle: string; dueAt: string }[]; expiring: { id: number; employeeName: string; courseTitle: string; expiresAt: string }[]; }
export interface Schedule { id: number; title: string; department: string | null; frequency: string; nextRunAt: string; isActive: boolean; }
export interface Delivery { id: number; title: string; createdAt: string; }
export interface AuditEvent { id: number; occurredAt: string; actorName: string; action: string; entityType: string; entityId: string; beforeJson: string | null; afterJson: string | null; }
export interface AuditPage { total: number; page: number; pageSize: number; items: AuditEvent[]; }
export interface ImportPreview { id: string; valid: boolean; expiresAt: string; rows: { row: number; action: string; employee: EmployeeInput; errors: string[] }[]; }
export interface Integrations { sso: { enabled: boolean; configured: boolean; authority: string | null; clientId: string | null; callbackPath: string }; mail: { enabled: boolean; configured: boolean; host: string | null; from: string | null }; hr: { configured: boolean }; registrationApproval: boolean; }
export type EnterpriseSection = 'organization' | 'accounts' | 'plans' | 'learning' | 'approvals' | 'materials' | 'notifications' | 'management-reports' | 'audit' | 'imports' | 'integrations' | 'settings';

export interface Operations { databaseReady: boolean; failedNotifications: number; pendingApprovals: number; unreadNotifications: number; overdueAssignments: number; runtime: { startedAt: string; requests: number; serverErrors: number; lastJobSuccess: string | null }; }
