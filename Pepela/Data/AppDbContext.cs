// AppDbContext.cs
// Author: Ondřej Ondryáš

using Microsoft.EntityFrameworkCore;

namespace Pepela.Data;

public class AppDbContext : DbContext
{
    public DbSet<ReservationEntity> Reservations { get; set; } = null!;

    public AppDbContext(DbContextOptions<AppDbContext> options)
        : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
    }
}