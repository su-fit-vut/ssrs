// EmailService.cs
// Author: Ondřej Ondryáš

using System.Globalization;
using System.Text;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;
using NodaTime;
using Pepela.Configuration;
using Pepela.Models;

namespace Pepela.Services;

public class EmailService
{
    #region Messages

    private const string ExtrasPartial = @"
<p>
Součástí tvé rezervace je taky:
</p>
<ul>
{0}
</ul>
";

    private const string ConfirmationMail = @"
<h2>Potvrď rezervaci místa</h2>
<p style=""font-weight: bold;"">Noc na FITu, 27. 9. 2024</p>
<p>
    Díky za rezervaci! Potvrď ji prosím kliknutím na odkaz:<br>
    <a style=""font-weight: bold;"" href=""{0}"">{0}</a>
</p>
{3}
<p>
    Nepotvrzené rezervace jsou platné pouze {1} minut od založení.<br>
    Dokud není rezervace potvrzená, můžeš na tento e-mail založit novou.<br>
    Rezervaci můžeš kdykoliv zrušit kliknutím <a href=""{2}"">sem</a>. 
</p>
<p>
    <br>Studentská unie FIT VUT v Brně
    <br><a href=""https://su.fit.vut.cz"">https://su.fit.vut.cz</a>
    <br>s případnými dotazy se ozvi na <a href=""mailto:xkucaj01@stud.fit.vut.cz"">xkucaj01@stud.fit.vut.cz</a>
nebo pomocí <a href=""https://su.fit.vut.cz/kontakt"">našeho kontaktního formuláře</a>
</p>
";

    private const string DoneMail = @"
<h2>Rezervace potvrzena</h2>
<p style=""font-weight: bold;"">Noc na FITu, 27. 9. 2024</p>
<p>
    Rezervace je potvrzena! Zarezervováno míst: {0}.<br>
    Prosím, kdyby se ti změnily plány, zruš rezervaci kliknutím <a href=""{1}"">na tento odkaz</a>.
</p>
{2}
<p>
    Budeme se těšit! Zatím se měj pěkně.
</p>
<p>
    
    <br>Studentská unie FIT VUT v Brně
    <br><a href=""https://su.fit.vut.cz"">https://su.fit.vut.cz</a>
    <br>s případnými dotazy se ozvi na <a href=""mailto:xkucaj01@stud.fit.vut.cz"">xkucaj01@stud.fit.vut.cz</a>
nebo pomocí <a href=""https://su.fit.vut.cz/kontakt"">našeho kontaktního formuláře</a>
</p>
";

    private const string CancellationMail = @"
<h2>Rezervace zrušena</h2>
<p style=""font-weight: bold;"">Noc na FITu, 27. 9. 2024</p>
<p>
    Tvá rezervace {1} z {0} byla zrušena, místo na akci bylo uvolněno.
</p>
<p>
    <br>Studentská unie FIT VUT v Brně
    <br><a href=""https://su.fit.vut.cz"">https://su.fit.vut.cz</a>
    <br>s případnými dotazy se ozvi na <a href=""mailto:xkucaj01@stud.fit.vut.cz"">xkucaj01@stud.fit.vut.cz</a>
nebo pomocí <a href=""https://su.fit.vut.cz/kontakt"">našeho kontaktního formuláře</a>
</p>
";

    private const string ReminderMail = @"
<h2>Noc na FITu</h2>
<p style=""font-weight: bold;"">27. 9. 2024</p>
<p>
    Ahoj! Už v&nbsp;pátek nás čeká Noc na FITu.
</p>

<h3>Organizační informace</h3>
<p>
    Celá akce bude začínat cca v&nbsp;18 hodin, stejně tak Noc vědců.
    U&nbsp;vstupu se nemusíš nijak prokazovat a můžeš přijít kudy chceš, akce probíhá po celé škole. Veškeré potřebné informace
     včetně programu se dozvíš na <a href=""https://www.instagram.com/sufitvut"">instagramu SU</a>.
</p>
<p>
    Máš zarezervováno {0}. Pokud víš, že nedojdeš, zruš prosím co nejdřív svou rezervaci kliknutím <a href=""{1}"">na tento odkaz</a>.
</p>
{2}
<p>
    Noc na FITu se bude odehrávat v&nbsp;areálu FIT VUT, ve kterém se nachází také koleje, dopřejme tedy prosím jejich
    obyvatelům v noci klid a&nbsp;umírněme zvukové projevy. Také připomínáme, že je zakázáno kouřit před vchodem do fakulty
     (jakož i&nbsp;v&nbsp;celém areálu fakulty), to platí i&nbsp;pro elektronické cigarety a&nbsp;obdobné záležitosti. V&nbsp;případě potřeby proto prosím využijte 
    <a href=""https://maps.app.goo.gl/NRwpXP4ReYYpKEHi7"">prostor před brankou</a>.
</p>
<p>
    <br>Studentská unie FIT VUT v Brně
    <br><a href=""https://su.fit.vut.cz"">https://su.fit.vut.cz</a>
    <br>s případnými dotazy se ozvi na <a href=""mailto:xkucaj01@stud.fit.vut.cz"">xkucaj01@stud.fit.vut.cz</a>
nebo pomocí <a href=""https://su.fit.vut.cz/kontakt"">našeho kontaktního formuláře</a>
</p>
";

    #endregion

    private readonly IOptionsSnapshot<SeatsOptions> _seatsOptions;
    private readonly ILogger<EmailService> _logger;
    private readonly MailOptions _options;

    public EmailService(IOptionsSnapshot<MailOptions> options, IOptionsSnapshot<SeatsOptions> seatsOptions,
        ILogger<EmailService> logger)
    {
        _seatsOptions = seatsOptions;
        _logger = logger;
        _options = options.Value;
    }

    private string MakeExtrasPartial(IEnumerable<TimeSlot>? extras)
    {
        if (extras == null)
            return string.Empty;

        var sb = new StringBuilder();
        foreach (var extra in extras)
        {
            sb.Append($"<li>{extra.ActivityName} ({extra.Start:HH:mm}–{extra.End:HH:mm})");
            if (extra.Note != null)
            {
                sb.Append(", ");
                sb.Append(extra.Note);
            }

            sb.AppendLine("</li>");
        }

        return string.Format(ExtrasPartial, sb);
    }

    public async Task SendConfirmationMail(string to, string confirmLink, string cancelLink,
        IEnumerable<TimeSlot>? extras)
    {
        var msg = string.Format(ConfirmationMail, confirmLink, _seatsOptions.Value.UnconfirmedValidMinutes,
            cancelLink, this.MakeExtrasPartial(extras));
        await this.SendAsync("Noc na FITu: Potvrď rezervaci", msg, to);
    }

    public async Task SendDoneMail(string to, int reservedSeats, string cancelLink, IEnumerable<TimeSlot>? extras)
    {
        var msg = string.Format(DoneMail, reservedSeats, cancelLink, this.MakeExtrasPartial(extras));
        await this.SendAsync("Noc na FITu: Rezervace potvrzena", msg, to, true);
    }

    public async Task SendCancelledEmail(string to, int seats, Instant madeOn)
    {
        var madeOnStr = madeOn.ToDateTimeUtc()
                              .ToLocalTime()
                              .ToString("dd. MM. HH:mm", CultureInfo.GetCultureInfo("cs-CZ"));
        var seatsStr = seats switch
        {
            1 => "jednoho místa",
            _ => $"{seats} míst"
        };
        var msg = string.Format(CancellationMail, madeOnStr, seatsStr);
        await this.SendAsync("Noc na FITu: Rezervace zrušena", msg, to);
    }

    public async Task SendReminderEmail(string to, int seats, string cancelLink, IEnumerable<TimeSlot>? extras)
    {
        var seatsStr = seats switch
        {
            1 => "jedno místo",
            < 5 => $"{seats} místa",
            _ => $"{seats} míst"
        };

        var msg = string.Format(ReminderMail, seatsStr, cancelLink, this.MakeExtrasPartial(extras));
        await this.SendAsync("Noc na FITu", msg, to);
    }

    private async Task<bool> SendAsync(string subject, string html, string to, bool bcc = false,
        CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(_options.Host))
        {
            await File.WriteAllTextAsync($"{to}.html", html, ct);
            _logger.LogInformation("Message {Sub} to {To}:\n{Html}", subject, to, html);

            return true;
        }

        try
        {
            // Initialize a new instance of the MimeKit.MimeMessage class
            var mail = new MimeMessage();

            mail.From.Add(new MailboxAddress(_options.DisplayName, _options.From));
            mail.Sender = new MailboxAddress(_options.DisplayName, _options.From);
            mail.To.Add(MailboxAddress.Parse(to));

            if (!string.IsNullOrEmpty(_options.ReplyTo))
                mail.ReplyTo.Add(new MailboxAddress(_options.ReplyToDisplayName, _options.ReplyTo));

            if (bcc && _options.BccTo != null)
                mail.Bcc.Add(MailboxAddress.Parse(_options.BccTo));

            // Add Content to Mime Message
            var body = new BodyBuilder();
            mail.Subject = subject;
            body.HtmlBody = html;
            mail.Body = body.ToMessageBody();

            using var smtp = new SmtpClient();

            if (_options.UseSsl)
                await smtp.ConnectAsync(_options.Host, _options.Port, SecureSocketOptions.SslOnConnect, ct);
            else if (_options.UseStartTls)
                await smtp.ConnectAsync(_options.Host, _options.Port, SecureSocketOptions.StartTls, ct);
            else
                await smtp.ConnectAsync(_options.Host, _options.Port, SecureSocketOptions.None, ct);

            if (_options is not ({ UserName: null } or { Password: null }))
                await smtp.AuthenticateAsync(_options.UserName, _options.Password, ct);

            await smtp.SendAsync(mail, ct);
            await smtp.DisconnectAsync(true, ct);

            return true;
        }
        catch (Exception e)
        {
            _logger.LogWarning(e, "Cannot send e-mail to {Recipient}.", to);

            return false;
        }
    }
}