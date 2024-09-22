// ReservationEntity.cs
// Author: Ondřej Ondryáš

using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace Pepela.Data;

[Index(nameof(Email), IsUnique = true)]
public class ReservationEntity
{
    [Key] public int Id { get; set; }

    [Required] public Instant MadeOn { get; set; }

    [Required] [StringLength(64)] public string ManagementToken { get; set; } = null!;

    [Required] public int Seats { get; set; }

    [Required] public string Email { get; set; } = null!;

    public Instant? CancelledOn { get; set; }
    public Instant? ConfirmedOn { get; set; }

    public bool SleepOver { get; set; } = false;
    [MaxLength(32)] public string? PubQuizTeamName { get; set; }
    public int PubQuizSeats { get; set; }
    public List<TimeSlotEntity> AssociatedTimeSlots { get; set; } = null!;
    public List<ReservationTimeSlotAssociation> TimeSlotAssociations { get; set; } = null!;

    [NotMapped] public bool Cancelled => CancelledOn != null;
    [NotMapped] public bool Confirmed => ConfirmedOn != null;
    [NotMapped] public bool HasPubQuizTeam => PubQuizTeamName != null;
}