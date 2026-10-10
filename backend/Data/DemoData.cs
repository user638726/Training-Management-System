using Microsoft.EntityFrameworkCore;
using TrainingManagement.Api.Models;

namespace TrainingManagement.Api.Data;

public static class DemoData
{
    public static async Task SeedAsync(TrainingDbContext db)
    {
        // Seed only an entirely new database; restarting preserves user edits.
        if (await db.Employees.AnyAsync() || await db.Courses.AnyAsync()) return;

        var employeeRows = new (string Name, string Department, string JobTitle)[]
        {
            ("陳怡君", "人力資源部", "人資經理"), ("林佳穎", "人力資源部", "訓練專員"),
            ("王思涵", "人力資源部", "人資專員"), ("黃子軒", "人力資源部", "招募專員"),
            ("李明哲", "資訊技術部", "技術主管"), ("張家豪", "資訊技術部", "軟體工程師"),
            ("吳佩蓉", "資訊技術部", "系統分析師"), ("許哲維", "資訊技術部", "資安工程師"),
            ("林宥廷", "業務行銷部", "業務經理"), ("蔡宜庭", "業務行銷部", "行銷企劃"),
            ("鄭博文", "業務行銷部", "客戶經理"), ("劉芷晴", "業務行銷部", "數位行銷專員"),
            ("陳冠宇", "營運管理部", "營運主管"), ("楊雅筑", "營運管理部", "專案管理師"),
            ("郭品妤", "營運管理部", "品質管理師"), ("曾柏翰", "營運管理部", "採購專員"),
            ("謝孟儒", "財務會計部", "財務經理"), ("洪詠恩", "財務會計部", "會計專員"),
            ("徐承恩", "業務行銷部", "業務專員"), ("何雨彤", "資訊技術部", "軟體工程師")
        };
        var employees = employeeRows.Select((row, index) => new Employee
        {
            EmployeeNumber = $"EMP{index + 1:000}", Name = row.Name,
            Email = $"employee{index + 1:00}@example.com", Department = row.Department,
            JobTitle = row.JobTitle, IsActive = index < 18
        }).ToList();
        db.Employees.AddRange(employees);

        var today = DateTime.UtcNow.AddHours(8).Date;
        var firstDay = new DateTime(today.Year, today.Month, 1);
        DateTime AtTaipei(DateTime date, int hour) =>
            DateTime.SpecifyKind(date.Date.AddHours(hour - 8), DateTimeKind.Utc);

        var completedTopics = new (string Title, string Category, string Instructor, decimal Hours)[]
        {
            ("職場溝通與跨部門協作", "職場素養", "陳怡君", 6),
            ("資訊安全與個人資料保護", "資訊技術", "許哲維", 3),
            ("專案管理與敏捷實務", "管理職能", "楊雅筑", 8),
            ("數據分析與商業決策", "數位能力", "李明哲", 6),
            ("客戶服務與問題解決", "業務行銷", "林宥廷", 4),
            ("新進員工企業文化與職安", "新人培訓", "林佳穎", 4)
        };
        var completedCourses = completedTopics.Select((topic, index) =>
        {
            var date = index < 5 ? firstDay.AddMonths(index - 5).AddDays(14) : today.AddDays(-3);
            return new Course
            {
                Title = topic.Title, Description = "透過案例分享、分組練習與情境討論，建立可應用於工作現場的能力。",
                Category = topic.Category, Instructor = topic.Instructor, Location = "總部 3F 培訓教室",
                Format = index % 2 == 0 ? CourseFormat.InPerson : CourseFormat.Hybrid,
                StartDate = AtTaipei(date, 9), EndDate = AtTaipei(date, 17),
                DurationHours = topic.Hours, Capacity = 24, Status = CourseStatus.Completed
            };
        }).ToList();
        var activeCourse = new Course
        {
            Title = "AI 工具應用與工作效率提升", Description = "學習使用 AI 整理資料、建立工作流程與提升日常協作效率，包含線上教材與實作作業。",
            Category = "數位能力", Instructor = "李明哲", Location = "線上學習平台", Format = CourseFormat.Online,
            StartDate = AtTaipei(today.AddDays(-2), 9), EndDate = AtTaipei(today.AddDays(7), 17),
            DurationHours = 6, Capacity = 30, Status = CourseStatus.Published
        };
        var upcomingTopics = new (string Title, string Category, string Instructor, CourseFormat Format, int Days, decimal Hours)[]
        {
            ("主管領導力與團隊發展", "管理職能", "陳怡君", CourseFormat.InPerson, 5, 8),
            ("Excel 數據整理與分析實務", "數位能力", "謝孟儒", CourseFormat.Hybrid, 9, 6),
            ("職場心理安全與壓力管理", "職場素養", "王思涵", CourseFormat.InPerson, 14, 3),
            ("永續經營與 ESG 基礎", "企業永續", "陳冠宇", CourseFormat.Online, 21, 4)
        };
        var upcomingCourses = upcomingTopics.Select(topic => new Course
        {
            Title = topic.Title, Description = "為企業同仁設計的實務課程，以工作案例帶領學員建立完整觀念並進行演練。",
            Category = topic.Category, Instructor = topic.Instructor,
            Location = topic.Format == CourseFormat.Online ? "線上會議室" : "總部 3F 培訓教室",
            Format = topic.Format, StartDate = AtTaipei(today.AddDays(topic.Days), 9),
            EndDate = AtTaipei(today.AddDays(topic.Days), 17), DurationHours = topic.Hours,
            Capacity = topic.Hours == 8 ? 12 : 24, Status = CourseStatus.Published
        }).ToList();
        var draft = new Course
        {
            Title = "進階專案風險管理工作坊", Description = "協助專案負責人辨識風險、建立應變方案，並練習利害關係人溝通。",
            Category = "管理職能", Instructor = "楊雅筑", Location = "總部 5F 會議室", Format = CourseFormat.InPerson,
            StartDate = AtTaipei(today.AddDays(28), 9), EndDate = AtTaipei(today.AddDays(28), 17),
            DurationHours = 6, Capacity = 18, Status = CourseStatus.Draft
        };
        db.Courses.AddRange(completedCourses);
        db.Courses.Add(activeCourse);
        db.Courses.AddRange(upcomingCourses);
        db.Courses.Add(draft);

        foreach (var (course, courseIndex) in completedCourses.Select((course, index) => (course, index)))
        {
            for (var index = 0; index < 7 + courseIndex; index++)
            {
                var employee = employees[(index + courseIndex * 2) % 18];
                db.Enrollments.Add(new Enrollment
                {
                    Course = course, Employee = employee, Status = EnrollmentStatus.Completed,
                    Progress = 100, Score = 78 + (index * 3 + courseIndex) % 23,
                    RegisteredAt = course.StartDate.AddDays(-7), CompletedAt = course.EndDate
                });
            }
        }
        for (var index = 0; index < 8; index++)
        {
            db.Enrollments.Add(new Enrollment
            {
                Course = activeCourse, Employee = employees[index], Status = EnrollmentStatus.InProgress,
                Progress = 20 + index * 10, RegisteredAt = AtTaipei(today.AddDays(-5), 10)
            });
        }
        foreach (var (course, courseIndex) in upcomingCourses.Select((course, index) => (course, index)))
        {
            for (var index = 0; index < 4 + courseIndex; index++)
            {
                db.Enrollments.Add(new Enrollment
                {
                    Course = course, Employee = employees[(index + courseIndex * 3) % 18],
                    Status = EnrollmentStatus.Registered, RegisteredAt = AtTaipei(today.AddDays(-index), 10)
                });
            }
        }
        db.Enrollments.Add(new Enrollment
        {
            Course = upcomingCourses[0], Employee = employees[16], Status = EnrollmentStatus.Cancelled,
            RegisteredAt = AtTaipei(today.AddDays(-1), 10)
        });
        await db.SaveChangesAsync();
    }
}
