// MailOptions.cs
// Author: Ondřej Ondryáš

namespace Pepela.Configuration;

public class MailOptions
{
    public required string DisplayName { get; set; }
    public required string From { get; set; }
    public string? ReplyTo { get; set; }
    public string? ReplyToDisplayName { get; set; }
    public string? BccTo { get; set; }
    public string? UserName { get; set; }
    public string? Password { get; set; }
    public required string Host { get; set; }
    public int Port { get; set; } = 587;
    public bool UseSsl { get; set; } = true;
    public bool UseStartTls { get; set; } = false;
}