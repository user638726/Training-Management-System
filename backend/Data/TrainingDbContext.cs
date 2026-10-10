using Microsoft.EntityFrameworkCore;
using TrainingManagement.Api.Models;

namespace TrainingManagement.Api.Data;

public sealed class TrainingDbContext(DbContextOptions<TrainingDbContext> options) : DbContext(options)
{
    public DbSet<Course> Courses => Set<Course>();
    public DbSet<Employee> Employees => Set<Employee>();
    public DbSet<Enrollment> Enrollments => Set<Enrollment>();
    public DbSet<AppUser> AppUsers => Set<AppUser>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Course>(entity =>
        {
            entity.Property(x => x.Title).HasMaxLength(160);
            entity.Property(x => x.Description).HasMaxLength(4000);
            entity.Property(x => x.Category).HasMaxLength(80);
            entity.Property(x => x.Instructor).HasMaxLength(100);
            entity.Property(x => x.Location).HasMaxLength(300);
            entity.Property(x => x.Format).HasConversion<string>().HasMaxLength(20);
            entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            entity.Property(x => x.DurationHours).HasPrecision(8, 2);
            entity.HasIndex(x => x.StartDate);
        });
        modelBuilder.Entity<Employee>(entity =>
        {
            entity.Property(x => x.EmployeeNumber).HasMaxLength(30);
            entity.Property(x => x.Name).HasMaxLength(100);
            entity.Property(x => x.Email).HasMaxLength(200);
            entity.Property(x => x.Department).HasMaxLength(80);
            entity.Property(x => x.JobTitle).HasMaxLength(100);
            entity.HasIndex(x => x.EmployeeNumber).IsUnique();
            entity.HasIndex(x => x.Email).IsUnique();
        });
        modelBuilder.Entity<Enrollment>(entity =>
        {
            entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            entity.Property(x => x.Score).HasPrecision(5, 2);
            entity.HasIndex(x => new { x.CourseId, x.EmployeeId }).IsUnique();
            entity.HasOne(x => x.Course).WithMany(x => x.Enrollments).HasForeignKey(x => x.CourseId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.Employee).WithMany(x => x.Enrollments).HasForeignKey(x => x.EmployeeId)
                .OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<AppUser>(entity =>
        {
            entity.Property(x => x.Username).HasMaxLength(80);
            entity.Property(x => x.NormalizedUsername).HasMaxLength(80);
            entity.Property(x => x.DisplayName).HasMaxLength(100);
            entity.Property(x => x.PasswordHash).HasMaxLength(512);
            entity.Property(x => x.Role).HasMaxLength(32);
            entity.HasIndex(x => x.NormalizedUsername).IsUnique();
            entity.HasIndex(x => x.EmployeeId).IsUnique().HasFilter("[EmployeeId] IS NOT NULL");
            entity.HasOne(x => x.Employee).WithOne(x => x.Account).HasForeignKey<AppUser>(x => x.EmployeeId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }
}
