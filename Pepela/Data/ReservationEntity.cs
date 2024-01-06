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

    [Required]
    [StringLength(64)]
    public string ManagementToken { get; set; } = null!;

    [Required]
    public int Seats { get; set; }

    [Required] public string Email { get; set; } = null!;

    public Instant? CancelledOn { get; set; }
    public Instant? ConfirmedOn { get; set; }

    [NotMapped] public bool Cancelled => this.CancelledOn != null;
    [NotMapped] public bool Confirmed => this.ConfirmedOn != null;
}