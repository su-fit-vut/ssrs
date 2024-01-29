// EmailService.cs
// Author: Ondřej Ondryáš

using System.Globalization;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;
using NodaTime;
using Pepela.Configuration;

namespace Pepela.Services;

public class EmailService
{
    #region Messages

    private const string ConfirmationMail = @"
<h2>Potvrď rezervaci místa</h2>
<p style=""font-weight: bold;"">Mucha v Kachně, 7. 2. 2024, 18:30</p>
<p>
    Díky za rezervaci! Potvrď ji prosím kliknutím na odkaz:<br>
    <a style=""font-weight: bold;"" href=""{0}"">{0}</a>
</p>
<p>
    Doporučené vstupné na koncert je 150 Kč. Můžeš však přispět více i méně.
</p>
<p>
    Nepotvrzené rezervace jsou platné pouze {1} minut od založení.<br>
    Dokud není rezervace potvrzená, můžeš na tento e-mail založit novou.<br>
    Rezervaci můžeš kdykoliv zrušit kliknutím <a href=""{2}"">sem</a>. 
</p>
<p>
    <br>Studentská unie FIT VUT v Brně
    <br><a href=""https://su.fit.vut.cz"">https://su.fit.vut.cz</a>
    <br>s případnými dotazy se ozvi na <a href=""mailto:xondry02@stud.fit.vut.cz"">xondry02@stud.fit.vut.cz</a>
nebo pomocí <a href=""https://su.fit.vut.cz/kontakt"">našeho kontaktního formuláře</a>
</p>
";

    private const string DoneMail = @"
<h2>Rezervace potvrzena</h2>
<p style=""font-weight: bold;"">Mucha v Kachně, 7. 2. 2024, 18:30</p>
<p>
    Rezervace je potvrzena! Zarezervováno míst: {0}.<br>
    Prosím, kdyby se ti změnily plány, zruš rezervaci kliknutím <a href=""{1}"">na tento odkaz</a>, ať máme představu,
    kolik lidí přijde.
</p>
<p>
    Doporučené vstupné na koncert je 150 Kč. Můžeš však přispět více i méně.<br>
    Budeme se těšit! Zatím se měj pěkně.
</p>
<p>
    
    <br>Studentská unie FIT VUT v Brně
    <br><a href=""https://su.fit.vut.cz"">https://su.fit.vut.cz</a>
    <br>s případnými dotazy se ozvi na <a href=""mailto:xondry02@stud.fit.vut.cz"">xondry02@stud.fit.vut.cz</a>
nebo pomocí <a href=""https://su.fit.vut.cz/kontakt"">našeho kontaktního formuláře</a>
</p>
";

    private const string CancellationMail = @"
<h2>Rezervace zrušena</h2>
<p style=""font-weight: bold;"">Mucha v Kachně, 7. 2. 2024, 18:30</p>
<p>
    Tvá rezervace {1} z {0} byla zrušena.
</p>
<p>
    <br>Studentská unie FIT VUT v Brně
    <br><a href=""https://su.fit.vut.cz"">https://su.fit.vut.cz</a>
    <br>s případnými dotazy se ozvi na <a href=""mailto:xondry02@stud.fit.vut.cz"">xondry02@stud.fit.vut.cz</a>
nebo pomocí <a href=""https://su.fit.vut.cz/kontakt"">našeho kontaktního formuláře</a>
</p>
";
    
    private const string ReminderMail = @"
<h2>Mucha v Kachně</h2>
<p style=""font-weight: bold;"">středa 7. 2. 2024, 18:30</p>
<p>
    Ahoj! Už ve středu nás čeká největší kulturní událost roku, koncert Nikoly Muchy ve studentském klubu U Kachničky na FIT VUT. Bude i překvapení!
</p>

<h3>Organizační informace</h3>
<p>
    Koncert by měl začít cca v 18:30. Kachna bude otevřena od <span style=""font-weight: bold;"">17:00</span>.<br>
    U vstupu stačí říct e-mail, pro který je rezervace vytvořená.<br>
    Doporučené vstupné na koncert je 150 Kč, platit lze jen v hotovosti. Pokud budeš mít přesně, budeme převelice šťastní.
</p>
<p>
    Máš zarezervováno {0}. Pokud víš, že nedojdeš, zruš prosím co nejdřív svou rezervaci kliknutím <a href=""{1}"">na tento odkaz</a>.
    Bez rezervace vstup do klubu nebude umožněn. 
</p>
<p>
    Kachna se nachází v místnosti R212 na FIT VUT. Odemknut by měl být 
    vchod do budovy P/R <a href=""https://maps.app.goo.gl/v9doZS8uP2tQbLYU9"">pod Kachnou</a>, ke kterému se dostaneš 
    brankou z ulice Metodějova. Pokud by bylo zamčeno, můžeš zavolat na <a href=""tel:+420541141013"">541 141 013</a>.
    Dovolujeme si připomenout, že Kachna zavírá ve 21:50. Nad klubem se nachází koleje, dopřejme tedy prosím jejich
    obyvatelům v noci klid. Také připomínáme, že je zakázáno kouřit před vchodem do fakulty (jakož i v celém areálu fakulty
    a klubu), to platí i pro elektronické cigarety a obdobné záležitosti. V případě potřeby proto prosím využijte 
    <a href=""https://maps.app.goo.gl/NRwpXP4ReYYpKEHi7"">prostor před brankou</a>.
</p>
<p>
    <br>Studentská unie FIT VUT v Brně
    <br><a href=""https://su.fit.vut.cz"">https://su.fit.vut.cz</a>
    <br>s případnými dotazy se ozvi na <a href=""mailto:xondry02@stud.fit.vut.cz"">xondry02@stud.fit.vut.cz</a>
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

    public async Task SendConfirmationMail(string to, string confirmLink, string cancelLink)
    {
        var msg = string.Format(ConfirmationMail, confirmLink, _seatsOptions.Value.UnconfirmedValidMinutes, cancelLink);
        await this.SendAsync("Potvrď rezervaci – Mucha v Kachně", msg, to);
    }

    public async Task SendDoneMail(string to, int reservedSeats, string cancelLink)
    {
        var msg = string.Format(DoneMail, reservedSeats, cancelLink);
        await this.SendAsync("Rezervace potvrzena – Mucha v Kachně", msg, to, true);
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
        await this.SendAsync("Rezervace zrušena – Mucha v Kachně", msg, to);
    }

    public async Task SendReminderEmail(string to, int seats, string cancelLink)
    {
        var seatsStr = seats switch
        {
            1 => "jedno místo",
            < 5 => $"{seats} místa",
            _ => $"{seats} míst"
        };
        
        var msg = string.Format(ReminderMail, seatsStr, cancelLink);
        await this.SendAsync("Mucha v Kachně", msg, to);
    }

    public async Task<bool> SendAsync(string subject, string html, string to, bool bcc = false, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(_options.Host))
        {
            File.WriteAllText($"{to}.html", html);
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