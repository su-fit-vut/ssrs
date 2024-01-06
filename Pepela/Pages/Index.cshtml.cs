using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;
using Pepela.Configuration;
using Pepela.Models;
using Pepela.Services;

namespace Pepela.Pages;

public class IndexModel : PageModel
{
    private readonly ReservationService _reservationService;
    private readonly IOptionsSnapshot<SeatsOptions> _seatsOptions;
    private readonly ILogger<IndexModel> _logger;

    [BindProperty] public required ReservationModel InputModel { get; set; }
    [BindNever] public int MaxSeats { get; }
    [BindNever] public ReservationAttemptResult? Result { get; set; } = null;
    [BindNever] public int SeatsLeft { get; set; }

    public IndexModel(ReservationService reservationService, IOptionsSnapshot<SeatsOptions> seatsOptions,
        ILogger<IndexModel> logger)
    {
        _reservationService = reservationService;
        _seatsOptions = seatsOptions;
        _logger = logger;

        MaxSeats = seatsOptions.Value.MaximumPerEmail;
    }

    public void OnGet(string? email)
    {
        InputModel = new ReservationModel()
        {
            Email = email ?? string.Empty,
            Seats = 1
        };
    }

    public async Task<IActionResult> OnPost()
    {
        if (InputModel.Seats < 1 || InputModel.Seats > _seatsOptions.Value.MaximumPerEmail)
            ModelState.AddModelError($"{nameof(InputModel)}.{nameof(InputModel.Seats)}",
                "Neplatný počet míst.");

        if (!ModelState.IsValid)
            return Page();

        Result = await _reservationService.MakeReservation(InputModel);
        
        if (Result == ReservationAttemptResult.NoSeatsLeft)
            SeatsLeft = await _reservationService.GetSeatsLeft();
            
        return Page();
    }
}