// ReservationModel.cs
// Author: Ondřej Ondryáš

using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;

namespace Pepela.Models;

public class ReservationModel
{
    [EmailAddress] public required string Email { get; set; }

    [Range(1, int.MaxValue, ErrorMessage = "Neplatný počet míst.")]
    public required int Seats { get; set; }

    [Required] public bool SleepOver { get; set; } = false;

    [MaxLength(32)]
    public string? PubQuizTeamName { get; set; }

    [Range(2, int.MaxValue, ErrorMessage = "Neplatný počet míst.")]
    public int? PubQuizSeats { get; set; }

    [BindProperty] public int? EscapeASelectedId { get; set; }
    [BindProperty] public int? EscapeBSelectedId { get; set; }
}