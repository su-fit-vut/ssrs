// Confirm.cshtml.cs
// Author: Ondřej Ondryáš

using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Pepela.Data;
using Pepela.Models;
using Pepela.Services;

namespace Pepela.Pages;

public class ConfirmModel : PageModel
{
    private readonly ReservationService _reservationService;

    [BindNever] public ReservationCompletionResult Result { get; set; }
    [BindNever] public ReservationEntity? Reservation { get; set; }
    [BindNever] public int SeatsLeft { get; set; }

    public ConfirmModel(ReservationService reservationService)
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

        Result = await _reservationService.ConfirmReservation(email, token);
        if (Result is ReservationCompletionResult.Confirmed or ReservationCompletionResult.AlreadyConfirmed)
            Reservation = await _reservationService.GetReservationDetails(email);
        else if (Result == ReservationCompletionResult.NoSeatsLeft)
            SeatsLeft = await _reservationService.GetSeatsLeft();
    }
}