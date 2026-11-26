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
        if (await FindChannelAsync(name, ct) is not { } channel)
        {
            return NotFound();
        }

        var videos = (await catalogue.PublishedAsync(ct)).Where(v => v.FolderId == channel.Id).ToList();
        var playlists = await catalogue.PlaylistsAsync(channel.Id, ct);
        return Page(await LandingPage.Channel(VideoDirectory.ChannelOf(channel), videos, await directory.AccountAsync(Host, ct), LandingPage.ClampPage(page, videos.Count), playlists));
    }

    /// <summary>One playlist's videos, the channel's page with the playlist's name (ADR 0012); no pages, a playlist is short.</summary>
    [HttpGet("c/{name}/{playlist}")]
    public async Task<IActionResult> Playlist(string name, string playlist, CancellationToken ct)
    {
        if (await FindChannelAsync(name, ct) is not { } channel
            || (await catalogue.PlaylistsAsync(channel.Id, ct)).FirstOrDefault(p => string.Equals(p.Name, playlist, StringComparison.OrdinalIgnoreCase)) is not { } found)
        {
            return NotFound();
        }

        var heading = VideoDirectory.ChannelOf(channel) with { Name = found.Name, Description = channel.Name };
        return Page(await LandingPage.Channel(heading, await catalogue.PlaylistVideosAsync(found.Id, ct), await directory.AccountAsync(Host, ct), 1, addressName: channel.Name));
    }

    // A channel by its NAME (owner decision 2026-11-26, AlcifraTube #28): the pages are the module's own, and sibling
    // uniqueness makes the name an unambiguous, reset-stable address. Case-insensitive, as document names are.
    private async Task<Alcifra.ModuleAbi.ModuleDocument?> FindChannelAsync(string name, CancellationToken ct) =>
        (await catalogue.ChannelsAsync(ct)).FirstOrDefault(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));

    [HttpHead("c/{name}/{playlist}")]
    public Task<IActionResult> PlaylistHead(string name, string playlist, CancellationToken ct) => Playlist(name, playlist, ct);

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
