using Microsoft.EntityFrameworkCore;

namespace Contractra.Infrastructure.Persistence;

public sealed class ContractraDbContext : DbContext
{
    public ContractraDbContext(
        DbContextOptions<ContractraDbContext> options)
        : base(options)
    {
    }
}