using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Recite.Data;

/// <summary>Design-time factory so <c>dotnet ef migrations</c> can build the model.</summary>
public sealed class ReciteDbContextFactory : IDesignTimeDbContextFactory<ReciteDbContext>
{
    public ReciteDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<ReciteDbContext>()
            .UseSqlite("Data Source=recite-design.sqlite")
            .Options;
        return new ReciteDbContext(options);
    }
}
