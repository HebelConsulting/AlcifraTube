using System.Globalization;
using System.Reflection;
using System.Text.Json;
using Alcifra.Tube.Api;
using Alcifra.Tube.Catalogue;
using Alcifra.Tube.Module;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.Extensions.Logging.Abstractions;
using Alcifra.ModuleAbi;

namespace Alcifra.Tube.Tests;

public class PeerTubeApiTests
{
    private static readonly Guid Tenant = Guid.Parse("22222222-3333-4444-8555-666666666666");
    private static readonly Guid Folder = Guid.Parse("77777777-8888-4999-8aaa-bbbbbbbbbbbb");
    private static readonly Guid Clip = Guid.Parse("cccccccc-dddd-4eee-8fff-000000000001");

    private static PeerTubeController Host(IReadOnlyList<ModuleDocument> documents)
    {
        var archive = new StubArchive(documents, new Dictionary<string, string> { [TubeSettings.NameKey] = "Kristall Tube", [TubeSettings.DescriptionKey] = "Our videos" }, id => new Uri($"https://store.test/{id:N}"));
        var caller = new StubCaller(Tenant);
        var catalogue = new VideoCatalogue(archive, caller, new EncryptionWarnings(), NullLogger<VideoCatalogue>.Instance);
        var http = new DefaultHttpContext();
        http.Request.Host = new HostString("video.example.test");
        return new PeerTubeController(catalogue, new VideoDirectory(archive, caller), caller) { ControllerContext = new ControllerContext { HttpContext = http } };
    }

    private static IReadOnlyList<ModuleDocument> Seed() =>
    [
        new(Folder, null, "Talks", TubeChannel.MaskId, new Dictionary<string, string>()),
        new(Clip, Folder, "Opening talk.mp4", Video.MaskId, new Dictionary<string, string>
        {
            [Video.Published] = "yes", [Video.PublishedAt] = "2026-10-01T09:00:00Z", [Video.Description] = "The opening", [Video.Tags] = "keynote, 2026", [Video.DurationSeconds] = "1800", [Video.Language] = "de",
        }),
        new(Guid.NewGuid(), Folder, "Draft.mp4", Video.MaskId, new Dictionary<string, string> { [Video.Published] = "no" }),
    ];

    private static JsonElement Json(IActionResult result) => JsonDocument.Parse(JsonSerializer.Serialize(Assert.IsType<OkObjectResult>(result).Value)).RootElement;

    [Fact]
    public async Task The_catalogue_lists_the_published_video_with_peertube_names_and_absolute_addresses()
    {
        var api = Host(Seed());
        var list = Json(await api.Videos(0, 15, CancellationToken.None));
        Assert.Equal(1, list.GetProperty("total").GetInt32());
        var item = list.GetProperty("data")[0];
        Assert.Equal(Clip.ToString("D"), item.GetProperty("uuid").GetString());
        Assert.Equal("Opening talk.mp4", item.GetProperty("name").GetString());
        Assert.Equal("2026-10-01T09:00:00.000Z", item.GetProperty("publishedAt").GetString());
        Assert.Equal("https://video.example.test/videos/watch/" + Clip.ToString("D"), item.GetProperty("url").GetString());
        Assert.Equal("t-" + Tenant.ToString("N"), item.GetProperty("account").GetProperty("name").GetString());
        Assert.Equal("video.example.test", item.GetProperty("account").GetProperty("host").GetString());
        Assert.Equal("Talks", item.GetProperty("channel").GetProperty("displayName").GetString());
        Assert.Equal("https://video.example.test/video-channels/f-" + Folder.ToString("N"), item.GetProperty("channel").GetProperty("url").GetString());
        Assert.False(item.GetProperty("isLive").GetBoolean());
        Assert.Equal(1800, item.GetProperty("duration").GetInt32());
    }

    [Fact]
    public async Task A_video_carries_its_one_file_on_the_file_door_and_its_description_tags_and_privacy()
    {
        var api = Host(Seed());
        var detail = Json(await api.Video(Clip, CancellationToken.None));
        var file = detail.GetProperty("files")[0];
        Assert.Equal($"https://video.example.test/files/{Clip:D}.mp4", file.GetProperty("fileUrl").GetString());
        Assert.Equal("Original", file.GetProperty("resolution").GetProperty("label").GetString());
        Assert.Equal("The opening", detail.GetProperty("description").GetString());
        Assert.Equal(["keynote", "2026"], detail.GetProperty("tags").EnumerateArray().Select(t => t.GetString()));
        // One flat object, as PeerTube's VideoDetails extends Video: NewPipe reads the name beside the files.
        Assert.Equal(1, detail.GetProperty("privacy").GetProperty("id").GetInt32());
        Assert.Equal("Opening talk.mp4", detail.GetProperty("name").GetString());
        Assert.Equal(Clip.ToString("D"), detail.GetProperty("uuid").GetString());
        Assert.Equal(1800, detail.GetProperty("duration").GetInt32());
        Assert.Equal("t-" + Tenant.ToString("N"), detail.GetProperty("account").GetProperty("name").GetString());
        Assert.False(detail.TryGetProperty("item", out _));
        Assert.Empty(detail.GetProperty("streamingPlaylists").EnumerateArray());
        Assert.IsType<NotFoundResult>(await api.Video(Guid.NewGuid(), CancellationToken.None));
        Assert.Equal("The opening", Json(await api.Description(Clip, CancellationToken.None)).GetProperty("description").GetString());
        Assert.Equal(0, Json(await api.Captions(Clip, CancellationToken.None)).GetProperty("total").GetInt32());
    }

    // What the PeerTube app (2.3.0) parses all-or-nothing. A missing path is the whole server refused, so the required paths
    // are named one by one, read from its models. Found on a real phone: "too old", then nothing would have parsed.
    [Theory]
    [InlineData("client.videos.miniature.preferAuthorDisplayName")]
    [InlineData("client.videos.resumableUpload.maxChunkSize")]
    [InlineData("client.menu.login.redirectOnSingleExternalAuth")]
    [InlineData("instance.name")]
    [InlineData("instance.shortDescription")]
    [InlineData("instance.isNSFW")]
    [InlineData("instance.defaultNSFWPolicy")]
    [InlineData("instance.defaultClientRoute")]
    [InlineData("search.remoteUri.users")]
    [InlineData("search.remoteUri.anonymous")]
    [InlineData("search.searchIndex.enabled")]
    [InlineData("search.searchIndex.url")]
    [InlineData("search.searchIndex.disableLocalSearch")]
    [InlineData("search.searchIndex.isDefaultSearch")]
    [InlineData("avatar.file.size.max")]
    [InlineData("avatar.file.extensions")]
    [InlineData("banner.file.size.max")]
    [InlineData("banner.file.extensions")]
    [InlineData("video.image.size.max")]
    [InlineData("video.image.extensions")]
    [InlineData("video.file.extensions")]
    [InlineData("videoCaption.file.size.max")]
    [InlineData("videoCaption.file.extensions")]
    [InlineData("user.videoQuota")]
    [InlineData("user.videoQuotaDaily")]
    [InlineData("import.videos.http.enabled")]
    [InlineData("import.videos.torrent.enabled")]
    [InlineData("import.videoChannelSynchronization.enabled")]
    [InlineData("import.users.enabled")]
    [InlineData("live.enabled")]
    [InlineData("live.allowReplay")]
    [InlineData("live.latencySetting.enabled")]
    [InlineData("live.maxDuration")]
    [InlineData("live.maxInstanceLives")]
    [InlineData("live.maxUserLives")]
    [InlineData("live.transcoding.enabled")]
    [InlineData("live.transcoding.remoteRunners.enabled")]
    [InlineData("live.transcoding.enabledResolutions")]
    [InlineData("live.transcoding.profile")]
    [InlineData("live.transcoding.availableProfiles")]
    [InlineData("live.rtmp.port")]
    [InlineData("defaults.publish.downloadEnabled")]
    [InlineData("defaults.publish.commentsPolicy")]
    [InlineData("defaults.publish.privacy")]
    [InlineData("defaults.p2p.webapp.enabled")]
    [InlineData("defaults.p2p.embed.enabled")]
    [InlineData("defaults.player.autoPlay")]
    [InlineData("signup.allowed")]
    [InlineData("signup.allowedForCurrentIP")]
    [InlineData("signup.requiresEmailVerification")]
    [InlineData("signup.minimumAge")]
    [InlineData("storyboards.enabled")]
    [InlineData("views.videos.watchingInterval.users")]
    [InlineData("plugin.registered")]
    [InlineData("plugin.registeredExternalAuths")]
    [InlineData("plugin.registeredIdAndPassAuths")]
    public async Task The_configuration_carries_every_path_the_app_requires(string path) =>
        Assert.NotEqual(JsonValueKind.Null, At(Json(await Host(Seed()).Config(CancellationToken.None)), path).ValueKind);

    // Optional in the app's model, so absent is fine; present, they must be complete, and the app refused a partial one.
    [Theory]
    [InlineData("transcoding")]
    [InlineData("theme")]
    public async Task The_configuration_leaves_out_the_optional_sections_it_cannot_fill(string section) =>
        Assert.False(Json(await Host(Seed()).Config(CancellationToken.None)).TryGetProperty(section, out _), section);

    [Fact]
    public async Task The_reported_version_passes_the_apps_minimum()
    {
        var reported = Json(await Host(Seed()).Config(CancellationToken.None)).GetProperty("serverVersion").GetString()!;
        Assert.True(new Version(reported.Split('-')[0]) >= new Version(7, 2, 0), reported);
    }

    [Theory]
    [InlineData("name")]
    [InlineData("shortDescription")]
    [InlineData("description")]
    [InlineData("terms")]
    [InlineData("codeOfConduct")]
    [InlineData("hardwareInformation")]
    [InlineData("creationReason")]
    [InlineData("moderationInformation")]
    [InlineData("administrator")]
    [InlineData("maintenanceLifetime")]
    [InlineData("businessModel")]
    [InlineData("languages")]
    [InlineData("categories")]
    [InlineData("banners")]
    [InlineData("avatars")]
    public async Task The_about_answer_carries_what_the_app_needs_to_switch_to_this_server(string field) =>
        Assert.True(Json(await Host(Seed()).About(CancellationToken.None)).GetProperty("instance").TryGetProperty(field, out _), field);

    [Fact]
    public async Task A_video_detail_carries_what_the_apps_model_requires_even_without_a_publish_date()
    {
        var undated = Guid.NewGuid();
        var api = Host([.. Seed(), new(undated, Folder, "Undated.mp4", Video.MaskId, new Dictionary<string, string> { [Video.Published] = "yes" })]);
        var detail = Json(await api.Video(undated, CancellationToken.None));

        foreach (var path in new[] { "createdAt", "updatedAt", "publishedAt", "account.createdAt", "account.updatedAt", "channel.createdAt", "channel.updatedAt" })
        {
            Assert.True(DateTimeOffset.TryParse(At(detail, path).GetString(), CultureInfo.InvariantCulture, out _), path);
        }

        Assert.False(detail.GetProperty("blacklisted").GetBoolean());
        Assert.Equal(2, Json(await api.Stats(CancellationToken.None)).GetProperty("totalLocalVideos").GetInt32());
    }

    private static JsonElement At(JsonElement root, string path) =>
        path.Split('.').Aggregate(root, (at, name) => at.GetProperty(name));

    [Fact]
    public async Task Channels_are_folders_the_account_is_the_tenant_and_search_reads_name_description_and_tags()
    {
        var api = Host(Seed());
        var channel = Json(await api.VideoChannel("f-" + Folder.ToString("N") + "@video.example.test", CancellationToken.None));
        Assert.Equal("Talks", channel.GetProperty("displayName").GetString());
        Assert.Equal(1, Json(await api.VideoChannelVideos("f-" + Folder.ToString("N"), 0, 15, CancellationToken.None)).GetProperty("total").GetInt32());
        Assert.IsType<NotFoundResult>(await api.VideoChannel("f-" + Guid.NewGuid().ToString("N"), CancellationToken.None));

        var account = Json(await api.AccountInfo("t-" + Tenant.ToString("N") + "@video.example.test", CancellationToken.None));
        Assert.Equal("Kristall Tube", account.GetProperty("displayName").GetString());
        Assert.Equal(1, Json(await api.AccountChannels("t-" + Tenant.ToString("N"), CancellationToken.None)).GetProperty("total").GetInt32());
        Assert.IsType<NotFoundResult>(await api.AccountInfo("someone", CancellationToken.None));

        Assert.Equal(1, Json(await api.SearchVideos("keynote", 0, 15, CancellationToken.None)).GetProperty("total").GetInt32());
        Assert.Equal(0, Json(await api.SearchVideos("cats", 0, 15, CancellationToken.None)).GetProperty("total").GetInt32());
        Assert.Equal("Kristall Tube", Json(await api.Config(CancellationToken.None)).GetProperty("instance").GetProperty("name").GetString());
    }

    // The contract (ADR 0004): every path the extractor requests is routed, GET and HEAD alike.
    [Theory]
    [InlineData("config")]
    [InlineData("videos")]
    [InlineData("videos/{videoId:guid}")]
    [InlineData("videos/{videoId:guid}/description")]
    [InlineData("videos/{videoId:guid}/captions")]
    [InlineData("videos/{videoId:guid}/chapters")]
    [InlineData("videos/{videoId:guid}/storyboards")]
    [InlineData("video-channels/{name}")]
    [InlineData("video-channels/{name}/videos")]
    [InlineData("video-channels/{name}/video-playlists")]
    [InlineData("accounts/{name}")]
    [InlineData("accounts/{name}/videos")]
    [InlineData("accounts/{name}/video-channels")]
    [InlineData("search/videos")]
    [InlineData("search/video-channels")]
    [InlineData("search/video-playlists")]
    public void Every_path_the_extractor_requests_is_routed_for_get_and_head(string template)
    {
        var methods = typeof(PeerTubeController).GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .SelectMany(m => m.GetCustomAttributes<HttpMethodAttribute>().Select(a => (a.Template, Method: a.HttpMethods.Single())))
            .ToList();
        Assert.Contains((template, "GET"), methods);
        Assert.Contains((template, "HEAD"), methods);
    }
}
