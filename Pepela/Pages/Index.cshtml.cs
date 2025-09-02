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
    [BindNever] public int MaxSeats { get; set; }
    [BindNever] public ReservationAttemptResult? Result { get; set; } = null;
    [BindNever] public int SeatsLeft { get; set; }
    [BindNever] public Dictionary<int, SlottedActivity> Slots { get; set; } = new();

    [BindProperty(Name = "email", SupportsGet = true)]
    public string? Email { get; set; }

    [BindProperty(Name = "token", SupportsGet = true)]
    public string? Token { get; set; }

    [BindNever] public bool EditMode { get; set; } = false;


    public IndexModel(ReservationService reservationService, IOptionsSnapshot<SeatsOptions> seatsOptions,
        ILogger<IndexModel> logger)
    {
        _reservationService = reservationService;
        _seatsOptions = seatsOptions;
        _logger = logger;

        MaxSeats = seatsOptions.Value.MaximumPerEmail;
    }

    public async Task OnGet()
    {
        InputModel = new ReservationModel()
        {
            Email = Email ?? string.Empty,
            Seats = 1
        };

        await this.InitModel(true);
    }

    public async Task<IActionResult> OnPost()
    {
        if (InputModel.Seats < 1 || InputModel.Seats > _seatsOptions.Value.MaximumPerEmail)
            ModelState.AddModelError($"{nameof(InputModel)}.{nameof(InputModel.Seats)}",
                "Neplatný počet rezervovaných míst.");

        // var pubQuizOk = true;
        // if (!string.IsNullOrWhiteSpace(InputModel.PubQuizTeamName))
        // {
        //     if (InputModel.PubQuizSeats is null)
        //         pubQuizOk = false;
        //
        //     (PubQuizTeamsAvailable, PubQuizSoloAvailable) = await _reservationService.GetPubQuizAvailability(false);
        //     if (InputModel.PubQuizSeats < MinPubQuizTeamSize
        //         || InputModel.PubQuizSeats > _seatsOptions.Value.MaxPubQuizTeamSize)
        //         pubQuizOk = false;
        // }
        //
        // if (!pubQuizOk)
        //     ModelState.AddModelError($"{nameof(InputModel)}.{nameof(InputModel.PubQuizSeats)}",
        //         "Neplatný počet členů týmu pro pubkvíz.");
        //
        // if (string.IsNullOrWhiteSpace(InputModel.PubQuizTeamName) && InputModel.PubQuizSeats is not (null or 0))
        //     ModelState.AddModelError($"{nameof(InputModel)}.{nameof(InputModel.PubQuizSeats)}",
        //         "Musíte zadat jméno týmu pro pubkvíz.");

        if (!ModelState.IsValid)
        {
            await this.InitModel(true);
            return Page();
        }

        Result = await _reservationService.MakeReservation(InputModel, updateToken: Token);
        await this.InitModel(false);

        return Page();
    }

    private async Task InitModel(bool cache)
    {
        SeatsLeft = await _reservationService.GetSeatsLeft(true);
        MaxSeats = int.Min(SeatsLeft, MaxSeats);

        var activities = await _reservationService.GetSlottedActivities();
        foreach (var activity in activities)
        {
            Slots.Add(activity.Id, activity);
        }

        if (Email != null && Token != null && InputModel.Email == Email)
        {
            var reservation = await _reservationService.GetReservationDetails(Email, Token);
            if (reservation != null)
            {
                EditMode = true;
                InputModel.Seats = reservation.Seats;
                InputModel.SleepOver = reservation.SleepOver;

                foreach (var slot in reservation.AssociatedTimeSlots)
                {
                    InputModel.SelectedTimeSlotIds[slot.ActivityId] = slot.Id;
                }
            }
        }
    }
}