using Alcifra.Tube.Catalogue;
using Alcifra.Tube.Module;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SimplArchive.ModuleAbi;

namespace Alcifra.Tube.Doors;

/// <summary>
/// The file door (ADR 0003): a video's stable file URL answers a <c>302</c> to a fresh presigned, inline storage link of
/// the right content type; the thumbnail likewise. No byte passes the server; an unpublished video is 404 on its next
/// request; nothing here is cached.
/// </summary>
[ApiController]
[AllowAnonymous]
[Route(TubeModule.RootPrefix + "/files")]
public sealed class FilesController(VideoCatalogue catalogue, IModuleArchiveFacade archive) : ControllerBase
{
    public static readonly TimeSpan LinkLifetime = TimeSpan.FromMinutes(5);

    private static readonly Dictionary<string, string> ImageTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["jpg"] = "image/jpeg",
        ["jpeg"] = "image/jpeg",
        ["png"] = "image/png",
        ["webp"] = "image/webp",
    };

    private static readonly Dictionary<string, string> ContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["mp4"] = "video/mp4",
        ["m4v"] = "video/mp4",
        ["webm"] = "video/webm",
        ["mov"] = "video/quicktime",
        ["mkv"] = "video/x-matroska",
        ["ogv"] = "video/ogg",
    };

    /// <summary>The content type a player is told for a stored extension; null for one this module does not serve.</summary>
    public static string? ContentTypeOf(string extension) => ContentTypes.GetValueOrDefault(extension.TrimStart('.'));

    [HttpGet("{videoId:guid}.{extension}")]
    public async Task<IActionResult> Video(Guid videoId, string extension, CancellationToken cancellationToken)
    {
        if (ContentTypeOf(extension) is not { } contentType || await catalogue.PublishedAsync(videoId, cancellationToken) is null)
        {
            return NotFound();
        }

        var link = await archive.GetDocumentContentUrlAsync(videoId, LinkLifetime, contentType, inline: true, cancellationToken);
        return link is null ? NotFound() : Fresh(link);
    }

    [HttpHead("{videoId:guid}.{extension}")]
    public Task<IActionResult> VideoHead(Guid videoId, string extension, CancellationToken cancellationToken) => Video(videoId, extension, cancellationToken);

    [HttpGet("{videoId:guid}/thumbnail")]
    public async Task<IActionResult> Thumbnail(Guid videoId, CancellationToken cancellationToken)
    {
        if (await catalogue.PublishedAsync(videoId, cancellationToken) is not { ThumbnailId: { } thumbnailId })
        {
            return NotFound();
        }

        var contentType = ImageTypes.GetValueOrDefault(await archive.GetDocumentContentExtensionAsync(thumbnailId, cancellationToken) ?? string.Empty, "image/jpeg");
        var link = await archive.GetDocumentContentUrlAsync(thumbnailId, LinkLifetime, contentType, inline: true, cancellationToken);
        return link is null ? NotFound() : Fresh(link);
    }

    [HttpHead("{videoId:guid}/thumbnail")]
    public Task<IActionResult> ThumbnailHead(Guid videoId, CancellationToken cancellationToken) => Thumbnail(videoId, cancellationToken);

    private IActionResult Fresh(Uri link)
    {
        Response.Headers.CacheControl = "no-store";
        return Redirect(link.ToString());
    }
}
