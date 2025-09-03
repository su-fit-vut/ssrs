// Cancel.cshtml.cs
// Author: Ondřej Ondryáš

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Pepela.Models;
using Pepela.Services;

namespace Pepela.Pages;

public class CancelModel : PageModel
{
    private readonly ReservationService _reservationService;
    private readonly IAuthorizationService _authorizationService;

    [BindNever] public ReservationCompletionResult Result { get; set; }
    [BindNever] public string ReservationEmail { get; set; } = string.Empty;

    public CancelModel(ReservationService reservationService, IAuthorizationService authorizationService)
    {
        _reservationService = reservationService;
        _authorizationService = authorizationService;
    }

    public async Task OnGet(string email, string token)
    {
        if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(token))
        {
            Result = ReservationCompletionResult.InvalidToken;

            return;
        }

        ReservationEmail = email.ToLowerInvariant();

        if (token == "_edit" && await _authorizationService.AuthorizeAsync(User, "IsAdmin") is { Succeeded: true })
        {
            // Admin override
            token = null!;
        }

        Result = await _reservationService.CancelReservation(email, token);
    }
}