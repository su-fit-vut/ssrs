// ReservationModel.cs
// Author: Ondřej Ondryáš

using System.ComponentModel.DataAnnotations;

namespace Pepela.Models;

public class ReservationModel
{
    [EmailAddress] public required string Email { get; set; }
    [Range(1, int.MaxValue, ErrorMessage = "Neplatný počet míst.")] public required int Seats { get; set; }
}