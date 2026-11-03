using System.Globalization;
using System.Text;
using Alcifra.Tube.Api;
using Alcifra.Tube.Module;
using static System.Net.WebUtility;

namespace Alcifra.Tube.Doors;

/// <summary>
/// The watch and embed pages (ADR 0005): the browser's own player on the file door, so the bytes still come from storage
/// by redirect, and the video's title, channel, account, date, description and tags as text. Every value is
/// HTML-encoded; there is no script.
/// </summary>
public static class WatchPage
{
    // No script anywhere; media and the poster come from the file door, which redirects to the storage host.
    public const string WatchPolicy = "default-src 'none'; media-src 'self' https:; img-src 'self' https:; style-src 'unsafe-inline'; frame-ancestors 'none'; base-uri 'none'; form-action 'none'";
    public const string EmbedPolicy = "default-src 'none'; media-src 'self' https:; img-src 'self' https:; style-src 'unsafe-inline'; frame-ancestors *; base-uri 'none'; form-action 'none'";

    public static string Watch(PublishedVideo video, Channel channel, Account account, string host, string extension)
    {
        var origin = $"https://{host}";
        var file = FileUrl(video, origin, extension);
        var poster = Poster(video, origin);
        var summary = video.Description.Length <= 200 ? video.Description : $"{video.Description[..200]}…";
        var page = new StringBuilder();
        page.Append(Head(video, account, $"{video.Name} · {account.Name}"));
        Meta(page, "og:type", "video.other");
        Meta(page, "og:site_name", account.Name);
        Meta(page, "og:title", video.Name);
        Meta(page, "og:description", summary);
        Meta(page, "og:url", $"{origin}/videos/watch/{video.Id:D}");
        Meta(page, "og:video", file);
        Meta(page, "og:video:type", FilesController.ContentTypeOf(extension) ?? "video/mp4");
        if (poster is not null)
        {
            Meta(page, "og:image", poster);
        }

        page.Append($"""<meta name="description" content="{HtmlEncode(summary)}">""").Append('\n');
        page.Append("""
            <style>
            body{margin:0;font:16px/1.5 system-ui,sans-serif;color:#1d1d1f;background:#fff}
            main{max-width:960px;margin:0 auto;padding:16px}
            video{width:100%;max-height:70vh;background:#000;border-radius:8px}
            h1{font-size:1.4rem;margin:12px 0 4px}
            .by{color:#6e6e73;margin:0 0 12px}
            .text{white-space:pre-wrap}
            .tags{display:flex;flex-wrap:wrap;gap:6px;padding:0;list-style:none}
            .tags li{background:#f2f2f4;border-radius:999px;padding:2px 10px;font-size:.85rem}
            @media (prefers-color-scheme:dark){body{color:#f5f5f7;background:#111}.by{color:#a1a1a6}.tags li{background:#2a2a2d}}
            </style>
            </head>
            <body>
            <main>

            """);
        page.Append(Player(file, poster)).Append('\n');
        page.Append($"<h1>{HtmlEncode(video.Name)}</h1>\n");
        page.Append($"""<p class="by">{HtmlEncode(channel.Name)} · {HtmlEncode(account.Name)}{Date(video.PublishedAt)}</p>""").Append('\n');
        if (video.Description.Length > 0)
        {
            page.Append($"""<p class="text">{HtmlEncode(video.Description)}</p>""").Append('\n');
        }

        if (video.Tags.Count > 0)
        {
            page.Append("""<ul class="tags">""");
            foreach (var tag in video.Tags)
            {
                page.Append($"<li>{HtmlEncode(tag)}</li>");
            }

            page.Append("</ul>\n");
        }

        page.Append("</main>\n</body>\n</html>\n");
        return page.ToString();
    }

    public static string Embed(PublishedVideo video, Account account, string host, string extension)
    {
        var origin = $"https://{host}";
        return $$"""
            {{Head(video, account, video.Name)}}<style>html,body{margin:0;height:100%;background:#000}video{width:100%;height:100%}</style>
            </head>
            <body>
            {{Player(FileUrl(video, origin, extension), Poster(video, origin))}}
            </body>
            </html>

            """;
    }

    private static string Head(PublishedVideo video, Account account, string title) => $"""
        <!doctype html>
        <html lang="{HtmlEncode(video.Language ?? "en")}">
        <head>
        <meta charset="utf-8">
        <meta name="viewport" content="width=device-width, initial-scale=1">
        <meta name="referrer" content="no-referrer">
        <title>{HtmlEncode(title)}</title>
        <meta name="author" content="{HtmlEncode(account.Name)}">

        """;

    private static string Player(string file, string? poster) =>
        $"""<video controls playsinline preload="metadata" src="{HtmlEncode(file)}"{(poster is null ? string.Empty : $" poster=\"{HtmlEncode(poster)}\"")}></video>""";

    private static void Meta(StringBuilder page, string property, string content) =>
        page.Append($"""<meta property="{property}" content="{HtmlEncode(content)}">""").Append('\n');

    private static string Date(DateTimeOffset? at) => at is { } stamp
        ? $""" · <time datetime="{stamp.ToUniversalTime():yyyy-MM-dd'T'HH:mm:ss'Z'}">{stamp.ToUniversalTime().ToString("d MMMM yyyy", CultureInfo.InvariantCulture)}</time>"""
        : string.Empty;

    private static string FileUrl(PublishedVideo video, string origin, string extension) => $"{origin}/files/{video.Id:D}.{extension}";

    private static string? Poster(PublishedVideo video, string origin) => video.ThumbnailId is null ? null : $"{origin}/files/{video.Id:D}/thumbnail";
}
