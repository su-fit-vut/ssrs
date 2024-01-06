// Cancel.cshtml.cs
// Author: Ondřej Ondryáš

using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Pepela.Models;
using Pepela.Services;

namespace Pepela.Pages;

public class CancelModel : PageModel
{
    private readonly ReservationService _reservationService;

    [BindNever] public ReservationCompletionResult Result { get; set; }
    [BindNever] public string ReservationEmail { get; set; } = string.Empty;

    public CancelModel(ReservationService reservationService)
    {
        _reservationService = reservationService;
    }

    public async Task OnGet(string email, string token)
    {
        if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(token))
        {
            Result = ReservationCompletionResult.InvalidToken;

            return;
        }

        ReservationEmail = email.ToLowerInvariant();
        Result = await _reservationService.CancelReservation(email, token);
    }
}