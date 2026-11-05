using Alcifra.Tube.Catalogue;
using Alcifra.Tube.Module;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Alcifra.Tube.Doors;

/// <summary>
/// The addresses a PeerTube video is shared and embedded by (ADR 0005): <c>/videos/watch/&lt;uuid&gt;</c>, the short
/// <c>/w/&lt;shortUUID&gt;</c> and <c>/videos/embed/&lt;uuid&gt;</c>, each a small page around the browser's own player.
/// Only a published video has one; nothing is cached, so an unpublished video's page is gone on its next request.
/// </summary>
[ApiController]
[AllowAnonymous]
[Route(TubeModule.RootPrefix)]
public sealed class WatchController(VideoCatalogue catalogue, VideoDirectory directory) : ControllerBase
{
    [HttpGet("videos/watch/{videoId:guid}")]
    public async Task<IActionResult> Watch(Guid videoId, CancellationToken ct)
    {
        if (await catalogue.PublishedAsync(videoId, ct) is not { } video)
        {
            return NotFound();
        }

        var account = await directory.AccountAsync(Host, ct);
        var channel = await directory.ChannelAsync(video, ct);
        return Page(await WatchPage.Watch(video, channel, account, Host, await directory.FileExtensionAsync(videoId, ct)), WatchPage.WatchPolicy);
    }

    [HttpHead("videos/watch/{videoId:guid}")]
    public Task<IActionResult> WatchHead(Guid videoId, CancellationToken ct) => Watch(videoId, ct);

    // Our shortUUID is the id without dashes (PeerTube's is base58; a client copies whatever the API handed it).
    [HttpGet("w/{shortId}")]
    public Task<IActionResult> Short(string shortId, CancellationToken ct) =>
        Guid.TryParseExact(shortId, "N", out var videoId) ? Watch(videoId, ct) : Task.FromResult<IActionResult>(NotFound());

    [HttpHead("w/{shortId}")]
    public Task<IActionResult> ShortHead(string shortId, CancellationToken ct) => Short(shortId, ct);

    [HttpGet("videos/embed/{videoId:guid}")]
    public async Task<IActionResult> Embed(Guid videoId, CancellationToken ct)
    {
        if (await catalogue.PublishedAsync(videoId, ct) is not { } video)
        {
            return NotFound();
        }

        var account = await directory.AccountAsync(Host, ct);
        return Page(await WatchPage.Embed(video, account, Host, await directory.FileExtensionAsync(videoId, ct)), WatchPage.EmbedPolicy);
    }

    [HttpHead("videos/embed/{videoId:guid}")]
    public Task<IActionResult> EmbedHead(Guid videoId, CancellationToken ct) => Embed(videoId, ct);

    private string Host => Request.Host.Host;

    private ContentResult Page(string html, string policy)
    {
        Response.Headers.ContentSecurityPolicy = policy;
        Response.Headers.CacheControl = "no-store";
        Response.Headers.XContentTypeOptions = "nosniff";
        return Content(html, "text/html; charset=utf-8");
    }
}
