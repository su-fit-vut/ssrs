// AppOptions.cs
// Author: Ondřej Ondryáš

namespace Pepela.Configuration;

public class AppOptions
{
    public required string PathBase { get; set; }
    public required string KnownProxyNetwork { get; set; }
    public required int KnownProxyPrefixLength { get; set; }
    public bool UseHttpsRedirection { get; set; } = true;
}