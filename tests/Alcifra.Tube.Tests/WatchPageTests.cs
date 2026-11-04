using Alcifra.Tube.Catalogue;
using Alcifra.Tube.Doors;
using Alcifra.Tube.Module;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using SimplArchive.ModuleAbi;

namespace Alcifra.Tube.Tests;

// The watch and embed pages (ADR 0005): the browser's own player on the file door, the video's text around it, every
// value encoded, no script, and only for a published video.
public class WatchPageTests
{
    private static readonly Guid Tenant = Guid.Parse("22222222-3333-4444-8555-666666666666");
    private static readonly Guid Folder = Guid.Parse("77777777-8888-4999-8aaa-bbbbbbbbbbbb");
    private static readonly Guid Clip = Guid.Parse("cccccccc-dddd-4eee-8fff-000000000001");
    private static readonly Guid Thumb = Guid.Parse("cccccccc-dddd-4eee-8fff-000000000002");
    private static readonly Guid Draft = Guid.Parse("cccccccc-dddd-4eee-8fff-000000000003");

    private static (WatchController Controller, DefaultHttpContext Http) Host()
    {
        IReadOnlyList<ModuleDocument> documents =
        [
            new(Folder, null, "Talks & <b>news</b>", TubeChannel.MaskId, new Dictionary<string, string>()),
            new(Clip, Folder, "Opening <script>alert(1)</script>.webm", Video.MaskId, new Dictionary<string, string>
            {
                [Video.Published] = "yes", [Video.PublishedAt] = "2026-10-01T09:00:00Z", [Video.Description] = "Line one\n\"quoted\" & more",
                [Video.Tags] = "keynote, 2026", [Video.Language] = "de", [Video.Thumbnail] = Thumb.ToString(),
            }),
            new(Draft, Folder, "Draft.mp4", Video.MaskId, new Dictionary<string, string> { [Video.Published] = "no" }),
        ];
        var archive = new StubArchive(documents, new Dictionary<string, string> { [TubeSettings.NameKey] = "Kristall Tube" });
        var caller = new StubCaller(Tenant);
        var catalogue = new VideoCatalogue(archive, caller, new EncryptionWarnings(), NullLogger<VideoCatalogue>.Instance);
        var http = new DefaultHttpContext();
        http.Request.Host = new HostString("video.example.test");
        return (new WatchController(catalogue, new VideoDirectory(archive, caller)) { ControllerContext = new ControllerContext { HttpContext = http } }, http);
    }

    private static string Html(IActionResult result) => Assert.IsType<ContentResult>(result).Content!;

    [Fact]
    public async Task The_watch_page_plays_the_file_door_and_shows_title_channel_account_date_description_and_tags()
    {
        var (watch, http) = Host();
        var result = Assert.IsType<ContentResult>(await watch.Watch(Clip, CancellationToken.None));

        Assert.Equal("text/html; charset=utf-8", result.ContentType);
        var html = result.Content!;
        Assert.Contains($"""<video controls playsinline preload="metadata" src="https://video.example.test/files/{Clip:D}.webm" poster="https://video.example.test/files/{Clip:D}/thumbnail"></video>""", html, StringComparison.Ordinal);
        Assert.Contains("<html lang=\"de\">", html, StringComparison.Ordinal);
        Assert.Contains($"""<a href="/c/f-{Folder:N}">Talks &amp; &lt;b&gt;news&lt;/b&gt;</a> · <a href="/">Kristall Tube</a>""", html, StringComparison.Ordinal);
        Assert.Contains("""<time datetime="2026-10-01T09:00:00Z">1 October 2026</time>""", html, StringComparison.Ordinal);
        Assert.Contains("Line one\n&quot;quoted&quot; &amp; more", html, StringComparison.Ordinal);
        Assert.Contains("<li>keynote</li><li>2026</li>", html, StringComparison.Ordinal);
        Assert.Contains("""<meta property="og:video:type" content="video/webm">""", html, StringComparison.Ordinal);
        Assert.Contains($"""<meta property="og:url" content="https://video.example.test/videos/watch/{Clip:D}">""", html, StringComparison.Ordinal);
        Assert.Equal(WatchPage.WatchPolicy, http.Response.Headers.ContentSecurityPolicy.ToString());
        Assert.Equal("no-store", http.Response.Headers.CacheControl.ToString());
    }

    [Fact]
    public async Task Nothing_a_document_says_becomes_markup_and_no_page_carries_a_script()
    {
        var (watch, _) = Host();
        var page = Html(await watch.Watch(Clip, CancellationToken.None));
        var embed = Html(await watch.Embed(Clip, CancellationToken.None));

        foreach (var html in new[] { page, embed })
        {
            Assert.DoesNotContain("<script", html, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("&lt;script&gt;", html, StringComparison.Ordinal);
        }

        // default-src 'none' with no script-src: the browser runs no script on either page, injected or not.
        foreach (var policy in new[] { WatchPage.WatchPolicy, WatchPage.EmbedPolicy })
        {
            Assert.StartsWith("default-src 'none';", policy, StringComparison.Ordinal);
            Assert.DoesNotContain("script-src", policy, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task The_short_address_is_the_id_without_dashes_and_the_embed_page_may_be_framed()
    {
        var (watch, http) = Host();
        Assert.Contains("<h1>", Html(await watch.Short(Clip.ToString("N"), CancellationToken.None)), StringComparison.Ordinal);
        Assert.IsType<NotFoundResult>(await watch.Short("not-an-id", CancellationToken.None));

        var embed = Html(await watch.Embed(Clip, CancellationToken.None));
        Assert.Contains($"src=\"https://video.example.test/files/{Clip:D}.webm\"", embed, StringComparison.Ordinal);
        Assert.DoesNotContain("<h1>", embed, StringComparison.Ordinal);
        Assert.Contains("frame-ancestors *", http.Response.Headers.ContentSecurityPolicy.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_unpublished_or_unknown_video_has_no_page()
    {
        var (watch, _) = Host();
        Assert.IsType<NotFoundResult>(await watch.Watch(Draft, CancellationToken.None));
        Assert.IsType<NotFoundResult>(await watch.Embed(Draft, CancellationToken.None));
        Assert.IsType<NotFoundResult>(await watch.Watch(Guid.NewGuid(), CancellationToken.None));
    }
}
