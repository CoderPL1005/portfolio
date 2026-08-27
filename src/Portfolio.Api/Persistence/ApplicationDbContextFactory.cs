using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Pgvector.EntityFrameworkCore;
using Portfolio.Infrastructure.Persistence;

namespace Portfolio.Api.Persistence;

public sealed class ApplicationDbContextFactory : IDesignTimeDbContextFactory<ApplicationDbContext>
{
    public ApplicationDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(
                "Host=localhost;Database=portfolio_design_time",
                npgsql => npgsql.UseVector())
            .Options;

        return new ApplicationDbContext(options);
    }
}
