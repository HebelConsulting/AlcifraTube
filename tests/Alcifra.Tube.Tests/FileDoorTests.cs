using Alcifra.Tube.Catalogue;
using Alcifra.Tube.Doors;
using Alcifra.Tube.Module;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Alcifra.ModuleAbi;

namespace Alcifra.Tube.Tests;

public class FileDoorTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid Channel = Guid.NewGuid();

    private static ModuleDocument VideoDocument(Guid id, bool published, string? publishedAt = null, Guid? thumbnail = null) =>
        new(id, Channel, "Clip", Video.MaskId, new Dictionary<string, string>
        {
            [Video.Published] = published ? "yes" : "no",
            [Video.PublishedAt] = publishedAt ?? string.Empty,
            [Video.Thumbnail] = thumbnail?.ToString("D") ?? string.Empty,
            [Video.Description] = "a clip",
        });

    private static (FilesController Door, VideoCatalogue Catalogue) Host(IReadOnlyList<ModuleDocument> documents, bool encrypts = false)
    {
        var archive = new StubArchive([new(Channel, null, "Talks", TubeChannel.MaskId, new Dictionary<string, string>()), .. documents], new Dictionary<string, string>(), id => new Uri($"https://store.test/{id:N}"));
        var catalogue = new VideoCatalogue(archive, new StubCaller(Tenant, encrypts), new EncryptionWarnings(), NullLogger<VideoCatalogue>.Instance);
        var door = new FilesController(catalogue, archive) { ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() } };
        return (door, catalogue);
    }

    [Fact]
    public async Task A_published_video_redirects_to_a_fresh_inline_link_of_its_type_and_is_never_cached()
    {
        var id = Guid.NewGuid();
        var (door, _) = Host([VideoDocument(id, published: true)]);
        var answer = Assert.IsType<RedirectResult>(await door.Video(id, "mp4", CancellationToken.None));
        Assert.Equal($"https://store.test/{id:N}?response-content-type=video%2Fmp4&disposition=inline", answer.Url);
        Assert.Equal("no-store", door.Response.Headers.CacheControl.ToString());
    }

    [Fact]
    public async Task An_unpublished_video_an_unknown_one_and_an_unserved_extension_are_not_found()
    {
        var id = Guid.NewGuid();
        var (door, _) = Host([VideoDocument(id, published: false)]);
        Assert.IsType<NotFoundResult>(await door.Video(id, "mp4", CancellationToken.None));
        Assert.IsType<NotFoundResult>(await door.Video(Guid.NewGuid(), "mp4", CancellationToken.None));
        var (published, _) = Host([VideoDocument(id, published: true)]);
        Assert.IsType<NotFoundResult>(await published.Video(id, "exe", CancellationToken.None));
    }

    [Fact]
    public async Task The_thumbnail_redirects_to_the_image_documents_content_and_is_absent_without_one()
    {
        var id = Guid.NewGuid();
        var image = Guid.NewGuid();
        var (door, _) = Host([VideoDocument(id, published: true, thumbnail: image)]);
        var answer = Assert.IsType<RedirectResult>(await door.Thumbnail(id, CancellationToken.None));
        Assert.StartsWith($"https://store.test/{image:N}?response-content-type=image%2Fjpeg", answer.Url);
        var (bare, _) = Host([VideoDocument(id, published: true)]);
        Assert.IsType<NotFoundResult>(await bare.Thumbnail(id, CancellationToken.None));
    }

    [Fact]
    public async Task The_catalogue_lists_published_videos_newest_first_and_is_empty_where_the_tenant_encrypts_at_rest()
    {
        var older = VideoDocument(Guid.NewGuid(), true, "2026-01-01T10:00:00Z");
        var newer = VideoDocument(Guid.NewGuid(), true, "2026-06-01T10:00:00Z");
        var draft = VideoDocument(Guid.NewGuid(), false, "2026-07-01T10:00:00Z");
        var (_, catalogue) = Host([older, draft, newer]);
        Assert.Equal([newer.Id, older.Id], (await catalogue.PublishedAsync(CancellationToken.None)).Select(v => v.Id));

        var (door, encrypting) = Host([newer], encrypts: true);
        Assert.Empty(await encrypting.PublishedAsync(CancellationToken.None));
        Assert.IsType<NotFoundResult>(await door.Video(newer.Id, "mp4", CancellationToken.None));
    }

    [Fact]
    public void The_module_is_free_serves_peertube_anonymously_on_the_tenants_host_and_stores_under_its_own_segment()
    {
        var module = new TubeModule();
        Assert.False(module.RequiresLicence);
        Assert.Equal(ModuleAbiVersion.Minor, module.AbiMinorVersion);
        Assert.Equal([("peertube", "tube.host")], module.AnonymousRootPrefixes.Select(p => (p.Prefix, p.HostSettingKey)));
        Assert.Equal("tube", Video.Mask.ContentSegment);
        Assert.Contains(module.Settings, s => s.Key == TubeSettings.HostKey);
        Assert.NotNull(module.Manual);
    }
}
