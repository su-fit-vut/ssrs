// KisAuthOptions.cs
// Author: Ondřej Ondryáš

namespace Pepela.Configuration;

public class KisAuthOptions
{
    public required string Authority { get; set; }
    public required string ClientId { get; set; }
    public required string ClientSecret { get; set; }
    public required string[] AdminDiscordIds { get; set; }
}