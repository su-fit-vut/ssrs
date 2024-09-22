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
                    .WithMany(r => r.AssociatedReservations)
                    .UsingEntity<ReservationTimeSlotAssociation>(
                        r => r.HasOne<TimeSlotEntity>(e => e.TimeSlot).WithMany(e => e.ReservationAssociations)
                            .OnDelete(DeleteBehavior.Cascade),
                        l => l.HasOne<ReservationEntity>(e => e.Reservation).WithMany(e => e.TimeSlotAssociations)
                            .OnDelete(DeleteBehavior.Cascade));

        modelBuilder.Entity<TimeSlotEntity>()
                    .HasOne(ts => ts.Activity)
                    .WithMany(a => a.TimeSlots)
                    .OnDelete(DeleteBehavior.Cascade);

        base.OnModelCreating(modelBuilder);
    }
}