using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Pepela.Models;
using Pepela.Services;

namespace Pepela.Pages;

[Authorize("IsAdmin")]
public class SlotInfo : PageModel
{
    private readonly ReservationService _reservationService;
    [BindNever] public TimeSlotWithReservations Slot { get; set; } = null!;

    public SlotInfo(ReservationService reservationService)
    {
        _reservationService = reservationService;
    }

    public async Task OnGet(int id)
    {
        var slot = await _reservationService.GetTimeSlotWithReservations(id);
        Slot = slot ?? throw new Exception("Slot not found: " + id);
    }

    public async Task OnGetCancel(int id, string email)
    {
        await _reservationService.RemoveTimeSlotFromReservation(email, id);
        await this.OnGet(id);
    }
}