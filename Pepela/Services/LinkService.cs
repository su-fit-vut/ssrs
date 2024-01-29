// LinkService.cs
// Author: Ondřej Ondryáš

using Microsoft.Extensions.Options;
using Pepela.Configuration;

namespace Pepela.Services;

public class LinkService
{
    private readonly LinkGenerator _linkGenerator;

    private readonly HttpContext? _context;
    private readonly string _scheme;
    private readonly HostString _host;
    private readonly PathString _path;

    public LinkService(LinkGenerator linkGenerator,
        IHttpContextAccessor httpContextAccessor,
        IOptions<LinkGenerationOptions> linkOptions)
    {
        _linkGenerator = linkGenerator;
        _context = httpContextAccessor.HttpContext;

        _scheme = linkOptions.Value.Scheme;
        _host = new HostString(linkOptions.Value.Host);
        _path = new PathString(linkOptions.Value.PathBase);
    }

    public string MakeConfirmLink(string mail, string token)
    {
        return _context == null
            ? _linkGenerator.GetUriByPage("Confirm", null, new { email = mail, token = token },
                _scheme, _host, _path)!
            : _linkGenerator.GetUriByPage(_context, "Confirm", null, new { email = mail, token = token })!;
    }

    public string MakeCancelLink(string mail, string token)
    {
        return _context == null
            ? _linkGenerator.GetUriByPage("Cancel", null, new { email = mail, token = token },
                _scheme, _host, _path)!
            : _linkGenerator.GetUriByPage(_context, "Cancel", null, new { email = mail, token = token })!;
    }
}