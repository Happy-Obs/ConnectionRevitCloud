using ConnectionRevitCloud.Server.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace ConnectionRevitCloud.Server.Data;

public class AppDbContext : DbContext
{
    public DbSet<User> Users => Set<User>();

    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<User>()
            .HasIndex(x => x.Username)
            .IsUnique();
    }
}
