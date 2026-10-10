export type CourseStatus = 'Draft' | 'Published' | 'Completed' | 'Cancelled';
export type CourseFormat = 'InPerson' | 'Online' | 'Hybrid';
export type EnrollmentStatus = 'Registered' | 'InProgress' | 'Completed' | 'Cancelled';
export interface Course {
  id: number; title: string; description: string; category: string; instructor: string; location: string;
  format: CourseFormat; startDate: string; endDate: string; durationHours: number; capacity: number;
  status: CourseStatus; enrolledCount: number; completedCount: number;
}
export type CourseInput = Omit<Course, 'id' | 'enrolledCount' | 'completedCount'>;
export interface Employee {
  id: number; employeeNumber: string; name: string; email: string; department: string; jobTitle: string; isActive: boolean;
}
export type EmployeeInput = Omit<Employee, 'id'>;
export interface Enrollment {
  id: number; courseId: number; employeeId: number; employeeName: string; employeeNumber: string;
  department: string; courseTitle: string; status: EnrollmentStatus; progress: number;
  score: number | null; registeredAt: string; completedAt: string | null;
}
export interface Dashboard {
  employeeCount: number; courseCount: number; activeCourseCount: number; enrollmentCount: number;
  completedEnrollmentCount: number; completionRate: number; totalTrainingHours: number;
  departmentStats: { department: string; employeeCount: number; enrollmentCount: number; completedCount: number; trainingHours: number }[];
  monthlyStats: { month: string; enrollments: number; completions: number }[];
}
export type Section = 'overview' | 'courses' | 'employees' | 'enrollments' | 'reports';
