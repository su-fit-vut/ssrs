// TimeSlotEntity.cs
// Author: Ondřej Ondryáš

using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using NodaTime;

namespace Pepela.Data;

public class TimeSlotEntity
{
    [Key] public int Id { get; set; }
    [Required] public Instant Start { get; set; }
    [Required] public Instant End { get; set; }
    [Required] public int TotalSeats { get; set; }
    [MaxLength(128)] public string? Note { get; set; }

    [Required] public int ActivityId { get; set; }
    public SlottedActivityEntity Activity { get; set; } = null!;
    public List<ReservationEntity> AssociatedReservations { get; set; } = null!;
    public bool AlwaysConsumeOnePerReservation { get; set; } = true;
}