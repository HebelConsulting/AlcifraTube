using Alcifra.Tube.Api;
using Alcifra.Tube.Module;

namespace Alcifra.Tube.Doors;

/// <summary>
/// The watch and embed pages (ADR 0005): the browser's own player on the file door, so the bytes still come from storage
/// by redirect, and the video's title, channel, account, date, description and tags as text. There is no script; the
/// views are Razor components (ADR 0009).
/// </summary>
public static class WatchPage
{
    // No script anywhere; media and the poster come from the file door, which redirects to the storage host.
    public const string WatchPolicy = "default-src 'none'; media-src 'self' https:; img-src 'self' https:; style-src 'unsafe-inline'; frame-ancestors 'none'; base-uri 'none'; form-action 'none'";
    public const string EmbedPolicy = "default-src 'none'; media-src 'self' https:; img-src 'self' https:; style-src 'unsafe-inline'; frame-ancestors *; base-uri 'none'; form-action 'none'";

    public static Task<string> Watch(PublishedVideo video, Channel channel, Account account, string host, string extension) =>
        PageRenderer.RenderAsync<WatchView>(new()
        {
            [nameof(WatchView.Video)] = video,
            [nameof(WatchView.Channel)] = channel,
            [nameof(WatchView.Account)] = account,
            [nameof(WatchView.Host)] = host,
            [nameof(WatchView.Extension)] = extension,
        });

    public static Task<string> Embed(PublishedVideo video, Account account, string host, string extension) =>
        PageRenderer.RenderAsync<EmbedView>(new()
        {
            [nameof(EmbedView.Video)] = video,
            [nameof(EmbedView.Account)] = account,
            [nameof(EmbedView.Host)] = host,
            [nameof(EmbedView.Extension)] = extension,
        });

    internal static string FileUrl(PublishedVideo video, string host, string extension) => $"https://{host}/files/{video.Id:D}.{extension}";

    internal static string? Poster(PublishedVideo video, string host) => video.ThumbnailId is null ? null : $"https://{host}/files/{video.Id:D}/thumbnail";
}
