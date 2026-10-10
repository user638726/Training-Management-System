namespace TrainingManagement.Api.Models;

public enum CourseFormat { InPerson, Online, Hybrid }
public enum CourseStatus { Draft, Published, Completed, Cancelled }
public enum EnrollmentStatus { Registered, InProgress, Completed, Cancelled }

public sealed class AppUser
{
    public int Id { get; set; }
    public string Username { get; set; } = "";
    public string NormalizedUsername { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public string Role { get; set; } = "Employee";
    public bool IsActive { get; set; } = true;
    public int TokenVersion { get; set; }
    public int? EmployeeId { get; set; }
    public Employee? Employee { get; set; }
}

public sealed class Course
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string Category { get; set; } = "";
    public string Instructor { get; set; } = "";
    public string Location { get; set; } = "";
    public CourseFormat Format { get; set; }
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public decimal DurationHours { get; set; }
    public int Capacity { get; set; }
    public CourseStatus Status { get; set; }
    public ICollection<Enrollment> Enrollments { get; set; } = new List<Enrollment>();
}

public sealed class Employee
{
    public int Id { get; set; }
    public string EmployeeNumber { get; set; } = "";
    public string Name { get; set; } = "";
    public string Email { get; set; } = "";
    public string Department { get; set; } = "";
    public string JobTitle { get; set; } = "";
    public bool IsActive { get; set; } = true;
    public AppUser? Account { get; set; }
    public ICollection<Enrollment> Enrollments { get; set; } = new List<Enrollment>();
}

public sealed class Enrollment
{
    public int Id { get; set; }
    public int CourseId { get; set; }
    public Course Course { get; set; } = null!;
    public int EmployeeId { get; set; }
    public Employee Employee { get; set; } = null!;
    public EnrollmentStatus Status { get; set; } = EnrollmentStatus.Registered;
    public int Progress { get; set; }
    public decimal? Score { get; set; }
    public DateTime RegisteredAt { get; set; }
    public DateTime? CompletedAt { get; set; }
}
