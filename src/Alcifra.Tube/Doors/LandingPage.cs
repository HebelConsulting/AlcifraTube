using System.Globalization;
using System.Text;
using Alcifra.Tube.Api;
using Alcifra.Tube.Module;
using static System.Net.WebUtility;

namespace Alcifra.Tube.Doors;

/// <summary>
/// The pages a browser lands on (ADR 0008): the host's latest videos, a channel's latest videos and the FAQ. The newest
/// video is the upper left card, the oldest the lower right. Like the watch page: every value HTML-encoded, no script.
/// </summary>
public static class LandingPage
{
    public const int PageSize = 24;

    // No script; images come from the file door, which redirects to the storage host.
    public const string Policy = "default-src 'none'; img-src 'self' https:; style-src 'unsafe-inline'; frame-ancestors 'none'; base-uri 'none'; form-action 'none'";

    private const string FaqQuestion = "How can I watch the videos offered here in a mobile app?";
    private const string NewPipeAddress = "https://newpipe.net/";
    private const string PeerTubeAddress = "https://apps.apple.com/app/peertube/id6737834858";

    private const string Style = """
        <style>
        body{margin:0;font:16px/1.5 system-ui,sans-serif;color:#1d1d1f;background:#fff}
        main{max-width:1280px;margin:0 auto;padding:16px}
        h1{font-size:1.5rem;margin:0}
        a{color:inherit}
        .intro{color:#6e6e73;margin:4px 0 16px;white-space:pre-wrap}
        .grid{display:grid;grid-template-columns:repeat(auto-fill,minmax(min(100%,260px),1fr));gap:20px;padding:0;margin:0;list-style:none}
        .thumb{display:block;width:100%;aspect-ratio:16/9;object-fit:cover;background:#2a2a2d;border-radius:8px}
        .open{display:block;text-decoration:none}
        h2{font-size:1rem;margin:8px 0 2px;overflow-wrap:anywhere}
        .by{color:#6e6e73;font-size:.875rem;margin:0}
        .text{font-size:.875rem;margin:4px 0 0;overflow-wrap:anywhere}
        nav{display:flex;flex-wrap:wrap;gap:16px;margin:24px 0 8px}
        @media (prefers-color-scheme:dark){body{color:#f5f5f7;background:#111}.intro,.by{color:#a1a1a6}}
        </style>
        </head>
        <body>
        <main>

        """;

    /// <summary>The address of a channel's page, which every surface showing a video links to.</summary>
    public static string ChannelAddress(Guid channelId) => $"/c/{PeerTubeShapes.ChannelName(channelId)}";

    /// <summary>The 1-based page, within what exists; anything else that was asked for is the first page.</summary>
    public static int ClampPage(int page, int videoCount) =>
        Math.Clamp(page, 1, Math.Max(1, (videoCount + PageSize - 1) / PageSize));

    public static string Latest(IReadOnlyList<PublishedVideo> videos, IReadOnlyDictionary<Guid, string> channelNames, Account account, int page)
    {
        var html = new StringBuilder(Head(account.Name));
        html.Append($"<h1>{HtmlEncode(account.Name)}</h1>\n");
        Intro(html, account.Description);
        Cards(html, videos, channelNames, page);
        Paging(html, "/", videos.Count, page);
        html.Append("""<nav><a href="/faq">FAQ</a></nav>""").Append('\n');
        return Foot(html);
    }

    public static string Channel(Channel channel, IReadOnlyList<PublishedVideo> videos, Account account, int page)
    {
        var html = new StringBuilder(Head($"{channel.Name} · {account.Name}"));
        html.Append($"<h1>{HtmlEncode(channel.Name)}</h1>\n");
        Intro(html, channel.Description ?? string.Empty);
        Cards(html, videos, new Dictionary<Guid, string> { [channel.FolderId] = channel.Name }, page);
        Paging(html, ChannelAddress(channel.FolderId), videos.Count, page);
        html.Append($"""<nav><a href="/">{HtmlEncode(account.Name)}</a><a href="/faq">FAQ</a></nav>""").Append('\n');
        return Foot(html);
    }

    public static string Faq(Account account)
    {
        var html = new StringBuilder(Head($"FAQ · {account.Name}"));
        html.Append("<h1>FAQ</h1>\n");
        html.Append($"<h2>{HtmlEncode(FaqQuestion)}</h2>\n");
        html.Append($"""<p>AlcifraTube has been tested with <a href="{NewPipeAddress}" rel="noopener noreferrer">NewPipe</a> and <a href="{PeerTubeAddress}" rel="noopener noreferrer">PeerTube</a>.</p>""").Append('\n');
        html.Append($"""<nav><a href="/">{HtmlEncode(account.Name)}</a></nav>""").Append('\n');
        return Foot(html);
    }

    private static string Head(string title) => $"""
        <!doctype html>
        <html lang="en">
        <head>
        <meta charset="utf-8">
        <meta name="viewport" content="width=device-width, initial-scale=1">
        <meta name="referrer" content="no-referrer">
        <title>{HtmlEncode(title)}</title>
        {Style}
        """;

    private static string Foot(StringBuilder html) => html.Append("</main>\n</body>\n</html>\n").ToString();

    private static void Intro(StringBuilder html, string text)
    {
        if (!string.IsNullOrWhiteSpace(text))
        {
            html.Append($"""<p class="intro">{HtmlEncode(text.Trim())}</p>""").Append('\n');
        }
    }

    private static void Cards(StringBuilder html, IReadOnlyList<PublishedVideo> videos, IReadOnlyDictionary<Guid, string> channelNames, int page)
    {
        if (videos.Count == 0)
        {
            html.Append("<p>No videos have been published yet.</p>\n");
            return;
        }

        html.Append("""<ul class="grid">""").Append('\n');
        foreach (var video in videos.Skip((page - 1) * PageSize).Take(PageSize))
        {
            var watch = $"/videos/watch/{video.Id:D}";
            var thumbnail = video.ThumbnailId is null
                ? """<span class="thumb"></span>"""
                : $"""<img class="thumb" src="/files/{video.Id:D}/thumbnail" alt="" loading="lazy">""";
            html.Append($"""<li><a class="open" href="{watch}">{thumbnail}<h2>{HtmlEncode(video.Name)}</h2></a>""");
            html.Append("""<p class="by">""");
            if (video.FolderId is { } channelId)
            {
                html.Append($"""<a href="{ChannelAddress(channelId)}">{HtmlEncode(channelNames.GetValueOrDefault(channelId, "Videos"))}</a>""");
            }

            html.Append(Date(video.PublishedAt)).Append("</p>");
            if (video.Description.Length > 0)
            {
                var text = video.Description.Length <= 200 ? video.Description : $"{video.Description[..200]}…";
                html.Append($"""<p class="text">{HtmlEncode(text)}</p>""");
            }

            html.Append("</li>\n");
        }

        html.Append("</ul>\n");
    }

    // Plain links, so the pages stay without script.
    private static void Paging(StringBuilder html, string address, int videoCount, int page)
    {
        var newer = page > 1;
        var older = page * PageSize < videoCount;
        if (!newer && !older)
        {
            return;
        }

        html.Append("<nav>");
        if (newer)
        {
            html.Append($"""<a href="{(page == 2 ? address : $"{address}?page={page - 1}")}" rel="prev">Newer</a>""");
        }

        if (older)
        {
            html.Append($"""<a href="{address}?page={page + 1}" rel="next">Older</a>""");
        }

        html.Append("</nav>\n");
    }

    private static string Date(DateTimeOffset? at) => at is { } stamp
        ? $""" · <time datetime="{stamp.ToUniversalTime():yyyy-MM-dd'T'HH:mm:ss'Z'}">{stamp.ToUniversalTime().ToString("d MMMM yyyy", CultureInfo.InvariantCulture)}</time>"""
        : string.Empty;
}
