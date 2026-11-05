using Alcifra.Tube.Api;
using Alcifra.Tube.Catalogue;
using Alcifra.Tube.Module;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Alcifra.Tube.Doors;

/// <summary>
/// The pages a browser lands on (ADR 0008): <c>/</c> is the host's latest videos, <c>/c/&lt;channel&gt;</c> a channel's and
/// <c>/faq</c> how to watch in a mobile app. Nothing is cached, so an unpublished video leaves them on the next request.
/// </summary>
[ApiController]
[AllowAnonymous]
[Route(TubeModule.RootPrefix)]
public sealed class LandingController(VideoCatalogue catalogue, VideoDirectory directory) : ControllerBase
{
    [HttpGet("")]
    public async Task<IActionResult> Latest([FromQuery] int page, CancellationToken ct)
    {
        var videos = await catalogue.PublishedAsync(ct);
        var names = (await catalogue.ChannelsAsync(ct)).ToDictionary(c => c.Id, c => c.Name);
        return Page(await LandingPage.Latest(videos, names, await directory.AccountAsync(Host, ct), LandingPage.ClampPage(page, videos.Count)));
    }

    [HttpHead("")]
    public Task<IActionResult> LatestHead([FromQuery] int page, CancellationToken ct) => Latest(page, ct);

    [HttpGet("c/{name}")]
    public async Task<IActionResult> Channel(string name, [FromQuery] int page, CancellationToken ct)
    {
        var channels = await catalogue.ChannelsAsync(ct);
        if (channels.FirstOrDefault(c => PeerTubeShapes.ChannelName(c.Id) == name) is not { } channel)
        {
            return NotFound();
        }

        var videos = (await catalogue.PublishedAsync(ct)).Where(v => v.FolderId == channel.Id).ToList();
        return Page(await LandingPage.Channel(VideoDirectory.ChannelOf(channel), videos, await directory.AccountAsync(Host, ct), LandingPage.ClampPage(page, videos.Count)));
    }

    [HttpHead("c/{name}")]
    public Task<IActionResult> ChannelHead(string name, [FromQuery] int page, CancellationToken ct) => Channel(name, page, ct);

    [HttpGet("faq")]
    public async Task<IActionResult> Faq(CancellationToken ct) => Page(await LandingPage.Faq(await directory.AccountAsync(Host, ct)));

    [HttpHead("faq")]
    public Task<IActionResult> FaqHead(CancellationToken ct) => Faq(ct);

    private string Host => Request.Host.Host;

    private ContentResult Page(string html)
    {
        Response.Headers.ContentSecurityPolicy = LandingPage.Policy;
        Response.Headers.CacheControl = "no-store";
        Response.Headers.XContentTypeOptions = "nosniff";
        return Content(html, "text/html; charset=utf-8");
    }
}
