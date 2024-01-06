// LinkService.cs
// Author: Ondřej Ondryáš

using Pepela.Models;

namespace Pepela.Services;

public class LinkService
{
    private readonly LinkGenerator _linkGenerator;
    private readonly HttpContext _context;

    public LinkService(LinkGenerator linkGenerator, IHttpContextAccessor httpContextAccessor)
    {
        _linkGenerator = linkGenerator;
        _context = httpContextAccessor.HttpContext ??
            throw new InvalidOperationException("No HTTP context available.");
    }

    public string MakeConfirmLink(string mail, string token)
    {
        return _linkGenerator.GetUriByPage(_context, "Confirm", null, new { email = mail, token = token })!;
    }

    public string MakeCancelLink(string mail, string token)
    {
        return _linkGenerator.GetUriByPage(_context, "Cancel", null, new { email = mail, token = token })!;
    }

    public string MakeNewLink(string? mail)
    {
        return _linkGenerator.GetUriByPage(_context, "Index", null, new { email = mail })!;
    }
}