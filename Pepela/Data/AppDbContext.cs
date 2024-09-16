// AppDbContext.cs
// Author: Ondřej Ondryáš

using Microsoft.EntityFrameworkCore;

namespace Pepela.Data;

public class AppDbContext : DbContext
{
    public DbSet<ReservationEntity> Reservations { get; set; } = null!;
    public DbSet<SlottedActivityEntity> Activities { get; set; } = null!;
    public DbSet<TimeSlotEntity> TimeSlots { get; set; } = null!;

    public AppDbContext(DbContextOptions<AppDbContext> options)
        : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ReservationEntity>()
                    .HasMany(r => r.AssociatedTimeSlots)
                    .WithMany(ts => ts.AssociatedReservations);

        modelBuilder.Entity<TimeSlotEntity>()
                    .HasOne(ts => ts.Activity)
                    .WithMany(a => a.TimeSlots)
                    .OnDelete(DeleteBehavior.Cascade);

        base.OnModelCreating(modelBuilder);
    }
}