// LinkOptions.cs
// Author: Ondřej Ondryáš

namespace Pepela.Configuration;

public class LinkGenerationOptions
{
    public required string Host { get; set; }
    public required string Scheme { get; set; }
    public required string PathBase { get; set; }
}