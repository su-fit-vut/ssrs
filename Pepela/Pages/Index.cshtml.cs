using System.ComponentModel.DataAnnotations;
using Humanizer;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;
using NodaTime;
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
    [BindNever] public int MaxSeats { get; set; }
    [BindNever] public ReservationAttemptResult? Result { get; set; } = null;
    [BindNever] public int SeatsLeft { get; set; }
    [BindNever] public List<TimeSlot> EscapeASlots { get; set; } = null!;
    [BindNever] public List<TimeSlot> EscapeBSlots { get; set; } = null!;
    [BindNever] public int PubQuizSeatsLeft { get; set; }

    public IndexModel(ReservationService reservationService, IOptionsSnapshot<SeatsOptions> seatsOptions,
        ILogger<IndexModel> logger)
    {
        _reservationService = reservationService;
        _seatsOptions = seatsOptions;
        _logger = logger;

        MaxSeats = seatsOptions.Value.MaximumPerEmail;
    }

    public async Task OnGet(string? email)
    {
        InputModel = new ReservationModel()
        {
            Email = email ?? string.Empty,
            Seats = 1
        };

        await this.InitModel(true);
    }

    public async Task<IActionResult> OnPost()
    {
        if (InputModel.Seats < 1 || InputModel.Seats > _seatsOptions.Value.MaximumPerEmail)
            ModelState.AddModelError($"{nameof(InputModel)}.{nameof(InputModel.Seats)}",
                "Neplatný počet rezervovaných míst.");

        if (!string.IsNullOrWhiteSpace(InputModel.PubQuizTeamName) && (InputModel.PubQuizSeats is null
                || InputModel.PubQuizSeats < _seatsOptions.Value.MinPubQuizTeamSize
                || InputModel.PubQuizSeats > _seatsOptions.Value.MaxPubQuizTeamSize))
            ModelState.AddModelError($"{nameof(InputModel)}.{nameof(InputModel.PubQuizSeats)}",
                "Neplatný počet členů týmu pro pubkvíz.");
        
        if (string.IsNullOrWhiteSpace(InputModel.PubQuizTeamName) && InputModel.PubQuizSeats is not (null or 0))
            ModelState.AddModelError($"{nameof(InputModel)}.{nameof(InputModel.PubQuizSeats)}",
                "Musíte zadat jméno týmu pro pubkvíz.");

        if (!ModelState.IsValid)
        {
            await this.InitModel(true);
            return Page();
        }

        Result = await _reservationService.MakeReservation(InputModel);
        await this.InitModel(false);

        return Page();
    }

    private async Task InitModel(bool cache)
    {
        SeatsLeft = await _reservationService.GetSeatsLeft(true);
        MaxSeats = int.Min(SeatsLeft, MaxSeats);
        PubQuizSeatsLeft = await _reservationService.GetPubQuizTeamsLeft(true);
        EscapeASlots = await _reservationService.GetTimeslotsForActivity(1);
        EscapeBSlots = await _reservationService.GetTimeslotsForActivity(2);
    }
}