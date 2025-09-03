// Confirm.cshtml.cs
// Author: Ondřej Ondryáš

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Pepela.Data;
using Pepela.Models;
using Pepela.Services;

namespace Pepela.Pages;

public class ConfirmModel : PageModel
{
    private readonly ReservationService _reservationService;
    private readonly IAuthorizationService _authorizationService;

    [BindNever] public ReservationCompletionResult Result { get; set; }
    [BindNever] public ReservationEntity? Reservation { get; set; }
    [BindNever] public int SeatsLeft { get; set; }

    public ConfirmModel(ReservationService reservationService, IAuthorizationService authorizationService)
    {
        _reservationService = reservationService;
        _authorizationService = authorizationService;
    }

    public async Task OnGet(string email, string token, bool force = false)
    {
        if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(token))
        {
            Result = ReservationCompletionResult.InvalidToken;

            return;
        }

        if (token == "_edit" && await _authorizationService.AuthorizeAsync(User, "IsAdmin") is { Succeeded: true })
        {
            // Admin override
            token = null!;
        }
        else
        {
            force = false;
        }

        Result = await _reservationService.ConfirmReservation(email, token, force);

        if (Result.Code is ReservationCompletionResultCode.Confirmed
            or ReservationCompletionResultCode.AlreadyConfirmed)
            Reservation = await _reservationService.GetReservationDetails(email);
        else if (Result == ReservationCompletionResult.NoSeatsLeft)
            SeatsLeft = await _reservationService.GetSeatsLeft();
    }
}