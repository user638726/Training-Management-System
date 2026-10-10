using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace TrainingManagement.Api.Data;

// Migration tooling needs the SQL Server model only; it does not start the API,
// issue tokens, access runtime secrets, or connect until an update is requested.
public sealed class TrainingDbContextFactory : IDesignTimeDbContextFactory<TrainingDbContext>
{
    public TrainingDbContext CreateDbContext(string[] args)
    {
        var connection = Environment.GetEnvironmentVariable("ConnectionStrings__TrainingDatabase")
            ?? "Server=localhost;Database=TrainingManagement;Trusted_Connection=True;Encrypt=True;TrustServerCertificate=True;Connect Timeout=10";
        return new TrainingDbContext(new DbContextOptionsBuilder<TrainingDbContext>().UseSqlServer(connection).Options);
    }
}
