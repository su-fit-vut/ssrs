// Admin.cshtml.cs
// Author: Ondřej Ondryáš

using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Pepela.Services;

namespace Pepela.Pages;

[Authorize("IsAdmin")]
public class AdminModel : PageModel
{
    private readonly ReservationService _reservationService;
    [TempData] public bool MailSent { get; set; }

    public AdminModel(ReservationService reservationService)
    {
        _reservationService = reservationService;
    }

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnGetSendMails(CancellationToken cancellationToken)
    {
        await _reservationService.SendReminderEmailToAll(cancellationToken);
        MailSent = true;

        return RedirectToPage("Admin");
    }

    public async Task<IActionResult> OnGetCsv()
    {
        var csv = await _reservationService.MakeConfirmedReservationsCsv();
        var bytes = Encoding.UTF8.GetBytes(csv);

        return File(bytes, "text/csv", "rezervace.csv");
    }
}