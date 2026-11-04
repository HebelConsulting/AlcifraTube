using Alcifra.Tube.Catalogue;
using Alcifra.Tube.Doors;
using Alcifra.Tube.Module;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using SimplArchive.ModuleAbi;

namespace Alcifra.Tube.Tests;

// The landing pages (ADR 0008): newest first, a link from every video to its channel, the channel's own page, plain
// paging, every value encoded, no script, and only what is published.
public class LandingPageTests
{
    private static readonly Guid Tenant = Guid.Parse("22222222-3333-4444-8555-666666666666");
    private static readonly Guid Talks = Guid.Parse("77777777-8888-4999-8aaa-000000000001");
    private static readonly Guid News = Guid.Parse("77777777-8888-4999-8aaa-000000000002");
    private static readonly Guid Older = Guid.Parse("cccccccc-dddd-4eee-8fff-000000000001");
    private static readonly Guid Newer = Guid.Parse("cccccccc-dddd-4eee-8fff-000000000002");
    private static readonly Guid Draft = Guid.Parse("cccccccc-dddd-4eee-8fff-000000000003");
    private static readonly Guid Thumb = Guid.Parse("cccccccc-dddd-4eee-8fff-000000000004");

    private static ModuleDocument Clip(Guid id, Guid channel, string name, string published, string at, string? thumbnail = null) =>
        new(id, channel, name, Video.MaskId, new Dictionary<string, string>
        {
            [Video.Published] = published,
            [Video.PublishedAt] = at,
            [Video.Description] = $"About <{name}>",
            [Video.Thumbnail] = thumbnail ?? string.Empty,
        });

    private static StubArchive Archive(params ModuleDocument[] videos) => new(
    [
        new(Talks, null, "Talks & <b>news</b>", TubeChannel.MaskId, new Dictionary<string, string> { [TubeChannel.Description] = "Our talks" }),
        new(News, null, "News", TubeChannel.MaskId, new Dictionary<string, string>()),
        .. videos,
    ], new Dictionary<string, string> { [TubeSettings.NameKey] = "Kristall Tube" });

    private static LandingController Host(StubArchive archive)
    {
        var caller = new StubCaller(Tenant);
        var catalogue = new VideoCatalogue(archive, caller, new EncryptionWarnings(), NullLogger<VideoCatalogue>.Instance);
        var http = new DefaultHttpContext();
        http.Request.Host = new HostString("video.example.test");
        return new LandingController(catalogue, new VideoDirectory(archive, caller)) { ControllerContext = new ControllerContext { HttpContext = http } };
    }

    private static string Html(IActionResult result) => Assert.IsType<ContentResult>(result).Content!;

    private static ModuleDocument[] Numbered(int count) =>
        [.. Enumerable.Range(1, count).Select(i => Clip(Guid.NewGuid(), Talks, $"Clip {i:D2}", "true", $"2026-01-{i:D2}T09:00:00Z"))];

    [Fact]
    public async Task The_front_page_starts_with_the_newest_and_shows_only_what_is_published()
    {
        var html = Html(await Host(Archive(
            Clip(Older, Talks, "Older", "true", "2026-09-01T09:00:00Z"),
            Clip(Newer, News, "Newer", "true", "2026-10-01T09:00:00Z", Thumb.ToString()),
            Clip(Draft, Talks, "Draft", "false", "2026-11-01T09:00:00Z"))).Latest(1, CancellationToken.None));

        Assert.True(html.IndexOf("<h2>Newer</h2>", StringComparison.Ordinal) < html.IndexOf("<h2>Older</h2>", StringComparison.Ordinal));
        Assert.DoesNotContain("Draft", html, StringComparison.Ordinal);
        Assert.Contains($"""<img class="thumb" src="/files/{Newer:D}/thumbnail" """, html, StringComparison.Ordinal);
        Assert.Contains($"""<a class="open" href="/videos/watch/{Newer:D}">""", html, StringComparison.Ordinal);
        Assert.Contains("About &lt;Newer&gt;", html, StringComparison.Ordinal);
        Assert.Contains("""<a href="/faq">FAQ</a>""", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_video_in_two_channels_appears_once_for_each_linking_to_that_channel()
    {
        var archive = Archive(Clip(Older, Talks, "Shared", "true", "2026-09-01T09:00:00Z"));
        archive.References.Add((News, Older, DateTimeOffset.UnixEpoch));

        var html = Html(await Host(archive).Latest(1, CancellationToken.None));

        Assert.Equal(2, html.Split("<h2>Shared</h2>").Length - 1);
        Assert.Contains($"""<a href="/c/f-{Talks:N}">Talks &amp; &lt;b&gt;news&lt;/b&gt;</a>""", html, StringComparison.Ordinal);
        Assert.Contains($"""<a href="/c/f-{News:N}">News</a>""", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_channel_page_lists_that_channels_videos_and_an_unknown_channel_has_none()
    {
        var landing = Host(Archive(
            Clip(Older, Talks, "In talks", "true", "2026-09-01T09:00:00Z"),
            Clip(Newer, News, "In news", "true", "2026-10-01T09:00:00Z")));

        var html = Html(await landing.Channel($"f-{Talks:N}", 1, CancellationToken.None));
        Assert.Contains("<h1>Talks &amp; &lt;b&gt;news&lt;/b&gt;</h1>", html, StringComparison.Ordinal);
        Assert.Contains("Our talks", html, StringComparison.Ordinal);
        Assert.Contains("<h2>In talks</h2>", html, StringComparison.Ordinal);
        Assert.DoesNotContain("In news", html, StringComparison.Ordinal);
        Assert.IsType<NotFoundResult>(await landing.Channel($"f-{Guid.NewGuid():N}", 1, CancellationToken.None));
        Assert.IsType<NotFoundResult>(await landing.Channel("Talks", 1, CancellationToken.None));
    }

    [Fact]
    public async Task Twenty_four_to_a_page_with_plain_links_and_a_wild_page_number_stays_within_what_exists()
    {
        var landing = Host(Archive(Numbered(25)));

        var first = Html(await landing.Latest(0, CancellationToken.None));
        Assert.Contains("<h2>Clip 25</h2>", first, StringComparison.Ordinal);
        Assert.Contains("<h2>Clip 02</h2>", first, StringComparison.Ordinal);
        Assert.DoesNotContain("<h2>Clip 01</h2>", first, StringComparison.Ordinal);
        Assert.Contains("""<a href="/?page=2" rel="next">Older</a>""", first, StringComparison.Ordinal);
        Assert.DoesNotContain("rel=\"prev\"", first, StringComparison.Ordinal);

        var last = Html(await landing.Latest(99, CancellationToken.None));
        Assert.Contains("<h2>Clip 01</h2>", last, StringComparison.Ordinal);
        Assert.Contains("""<a href="/" rel="prev">Newer</a>""", last, StringComparison.Ordinal);
        Assert.DoesNotContain("rel=\"next\"", last, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_faq_answers_the_question_and_no_page_carries_a_script()
    {
        var landing = Host(Archive(Clip(Older, Talks, "<script>alert(1)</script>", "true", "2026-09-01T09:00:00Z")));
        var faq = Html(await landing.Faq(CancellationToken.None));
        Assert.Contains("How can I watch the videos offered here in a mobile app?", faq, StringComparison.Ordinal);
        Assert.Contains("AlcifraTube has been tested with", faq, StringComparison.Ordinal);
        Assert.Contains("https://apps.apple.com/app/peertube/id6737834858", faq, StringComparison.Ordinal);

        foreach (var html in new[] { faq, Html(await landing.Latest(1, CancellationToken.None)), Html(await landing.Channel($"f-{Talks:N}", 1, CancellationToken.None)) })
        {
            Assert.DoesNotContain("<script", html, StringComparison.OrdinalIgnoreCase);
        }

        Assert.StartsWith("default-src 'none';", LandingPage.Policy, StringComparison.Ordinal);
        Assert.DoesNotContain("script-src", LandingPage.Policy, StringComparison.Ordinal);
        Assert.Equal(LandingPage.Policy, landing.Response.Headers.ContentSecurityPolicy.ToString());
    }
}
