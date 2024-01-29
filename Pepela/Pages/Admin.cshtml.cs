// Admin.cshtml.cs
// Author: Ondřej Ondryáš

using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Pepela.Services;

namespace Pepela.Pages;

[Authorize(Roles = "ExecutiveMember")]
public class AdminModel : PageModel
{
    private readonly ReservationService _reservationService;
    public bool MailSent { get; set; }

    public AdminModel(ReservationService reservationService)
    {
        _reservationService = reservationService;
    }

    public void OnGet()
    {
    }

    public async Task OnGetSendMails(CancellationToken cancellationToken)
    {
        await _reservationService.SendReminderEmailToAll(cancellationToken);
        MailSent = true;
    }

    public async Task<IActionResult> OnGetCsv()
    {
        var csv = await _reservationService.MakeConfirmedReservationsCsv();
        var bytes = Encoding.UTF8.GetBytes(csv);

        return File(bytes, "text/csv", "rezervace.csv");
    }
}