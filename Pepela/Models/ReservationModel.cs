// ReservationModel.cs
// Author: Ondřej Ondryáš

using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using NodaTime;

namespace Pepela.Models;

public class ReservationModel
{
    [EmailAddress] public required string Email { get; set; }

    [Range(1, int.MaxValue, ErrorMessage = "Neplatný počet míst.")]
    public required int Seats { get; set; }

    // public bool SleepOver { get; set; } = false;

    [MaxLength(32)] public string? PubQuizTeamName { get; set; }

    [Range(2, int.MaxValue, ErrorMessage = "Neplatný počet míst.")]
    public int? PubQuizSeats { get; set; }

    public bool PubQuizReserveSolo { get; set; }

    [BindProperty] public Dictionary<int, int?> SelectedTimeSlotIds { get; set; } = new();

    [BindNever]
    public bool WantsPubQuiz =>
        !string.IsNullOrWhiteSpace(PubQuizTeamName) || PubQuizReserveSolo;
}

public record ReservationOverview
{
    public required string Email { get; init; }
    public required int Seats { get; init; }
    public required bool SleepOver { get; init; }
    public required bool Confirmed { get; init; }
    public required bool Cancelled { get; init; }
    public required ZonedDateTime MadeOn { get; init; }
}