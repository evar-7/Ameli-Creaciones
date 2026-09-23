using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Ameli.Api.Infrastructure;

public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<SecurityDbContext>
{
    public SecurityDbContext CreateDbContext(string[] args) => new(new DbContextOptionsBuilder<SecurityDbContext>()
        .UseSqlServer(Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection")
            ?? "Server=(localdb)\\MSSQLLocalDB;Database=AmeliSecurity;Trusted_Connection=True;TrustServerCertificate=True")
        .Options);
}
