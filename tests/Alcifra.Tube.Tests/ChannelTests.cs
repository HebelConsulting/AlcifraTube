using System.Text.Json;
using Alcifra.Tube.Api;
using Alcifra.Tube.Catalogue;
using Alcifra.Tube.Doors;
using Alcifra.Tube.Module;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Alcifra.ModuleAbi;

namespace Alcifra.Tube.Tests;

// Channels and references (ADR 0006): a video is prepared anywhere, and offered only by sitting in a Tube Channel or being
// referenced from one; the overview has one entry per channel, and a request by id names one fixed channel.
public class ChannelTests
{
    private static readonly Guid Tenant = Guid.Parse("22222222-3333-4444-8555-666666666666");
    private static readonly Guid Talks = Guid.Parse("10000000-0000-4000-8000-000000000001");
    private static readonly Guid Films = Guid.Parse("10000000-0000-4000-8000-000000000002");
    private static readonly Guid Elsewhere = Guid.Parse("10000000-0000-4000-8000-000000000003");
    private static readonly Guid Inside = Guid.Parse("20000000-0000-4000-8000-000000000001");
    private static readonly Guid Prepared = Guid.Parse("20000000-0000-4000-8000-000000000002");
    private static readonly Guid Unreferenced = Guid.Parse("20000000-0000-4000-8000-000000000003");
    private static readonly Guid Draft = Guid.Parse("20000000-0000-4000-8000-000000000004");
    private static readonly Guid Photo = Guid.Parse("20000000-0000-4000-8000-000000000005");

    private static Dictionary<string, string> Published(string at, string? thumbnail = null) => new()
    {
        [Video.Published] = "yes",
        [Video.PublishedAt] = at,
        [Video.Thumbnail] = thumbnail ?? string.Empty,
    };

    private static StubArchive Archive() => new(
    [
        new(Talks, null, "Talks", TubeChannel.MaskId, new Dictionary<string, string> { [TubeChannel.Description] = " What was said. " }),
        new(Films, null, "Films", TubeChannel.MaskId, new Dictionary<string, string>()),
        new(Elsewhere, null, "Drafts folder", null, new Dictionary<string, string>()),
        new(Inside, Talks, "Inside.mp4", Video.MaskId, Published("2026-10-01T09:00:00Z")),
        new(Prepared, Elsewhere, "Prepared", Video.MaskId, Published("2026-10-02T09:00:00Z", Photo.ToString("D"))),
        new(Unreferenced, Elsewhere, "Unreferenced.mp4", Video.MaskId, Published("2026-10-03T09:00:00Z")),
        new(Draft, Talks, "Draft.mp4", Video.MaskId, new Dictionary<string, string> { [Video.Published] = "no" }),
        new(Photo, Elsewhere, "Photo", VideoThumbnail.MaskId, new Dictionary<string, string>()),
    ], new Dictionary<string, string> { [TubeSettings.NameKey] = "Kristall Tube" }, id => new Uri($"https://store.test/{id:N}"));

    private static (PeerTubeController Api, FilesController Door, WatchController Watch) Host(StubArchive archive, bool encrypts = false)
    {
        var caller = new StubCaller(Tenant, encrypts);
        var catalogue = new VideoCatalogue(archive, caller, new EncryptionWarnings(), NullLogger<VideoCatalogue>.Instance);
        var directory = new VideoDirectory(archive, caller);
        ControllerContext Context()
        {
            var http = new DefaultHttpContext();
            http.Request.Host = new HostString("video.example.test");
            return new ControllerContext { HttpContext = http };
        }

        return (new PeerTubeController(catalogue, directory, caller) { ControllerContext = Context() },
            new FilesController(catalogue, archive) { ControllerContext = Context() },
            new WatchController(catalogue, directory) { ControllerContext = Context() });
    }

    private static JsonElement Json(IActionResult result) => JsonDocument.Parse(JsonSerializer.Serialize(Assert.IsType<OkObjectResult>(result).Value)).RootElement;

    private static List<(string Uuid, string Channel)> Entries(JsonElement page) =>
        [.. page.GetProperty("data").EnumerateArray().Select(v => (v.GetProperty("uuid").GetString()!, v.GetProperty("channel").GetProperty("displayName").GetString()!))];

    [Fact]
    public async Task A_published_video_outside_every_channel_is_offered_nowhere()
    {
        var (api, door, watch) = Host(Archive());

        Assert.DoesNotContain(Entries(Json(await api.Videos(0, 15, default))), e => e.Uuid == Unreferenced.ToString("D"));
        Assert.IsType<NotFoundResult>(await api.Video(Unreferenced, default));
        Assert.IsType<NotFoundResult>(await door.Video(Unreferenced, "mp4", default));
        Assert.IsType<NotFoundResult>(await watch.Watch(Unreferenced, default));
    }

    [Fact]
    public async Task A_reference_offers_the_video_in_that_channel_and_removing_it_ends_that()
    {
        var archive = Archive();
        var (api, door, _) = Host(archive);
        Assert.IsType<NotFoundResult>(await api.Video(Prepared, default));

        archive.References.Add((Talks, Prepared, DateTimeOffset.UtcNow));
        Assert.Contains((Prepared.ToString("D"), "Talks"), Entries(Json(await api.Videos(0, 15, default))));
        Assert.IsType<RedirectResult>(await door.Video(Prepared, "mp4", default));

        archive.References.Clear();
        Assert.IsType<NotFoundResult>(await api.Video(Prepared, default));
    }

    [Fact]
    public async Task The_overview_has_one_entry_per_channel_each_naming_it_and_a_channels_list_only_its_own()
    {
        var archive = Archive();
        archive.References.Add((Talks, Prepared, DateTimeOffset.UtcNow.AddDays(-2)));
        archive.References.Add((Films, Prepared, DateTimeOffset.UtcNow.AddDays(-1)));
        var (api, _, _) = Host(archive);

        var overview = Json(await api.Videos(0, 15, default));
        Assert.Equal(3, overview.GetProperty("total").GetInt32());
        var entries = Entries(overview);
        Assert.Contains((Prepared.ToString("D"), "Talks"), entries);
        Assert.Contains((Prepared.ToString("D"), "Films"), entries);
        Assert.Contains((Inside.ToString("D"), "Talks"), entries);

        var films = Entries(Json(await api.VideoChannelVideos(PeerTubeShapes.ChannelName(Films), 0, 15, default)));
        Assert.Equal([(Prepared.ToString("D"), "Films")], films);
        var talks = Entries(Json(await api.VideoChannelVideos(PeerTubeShapes.ChannelName(Talks), 0, 15, default)));
        Assert.Equal(2, talks.Count);
        Assert.All(talks, e => Assert.Equal("Talks", e.Channel));
    }

    [Fact]
    public async Task A_request_by_id_names_where_it_sits_else_its_oldest_reference()
    {
        var archive = Archive();
        archive.References.Add((Films, Prepared, DateTimeOffset.UtcNow.AddDays(-1)));
        archive.References.Add((Talks, Prepared, DateTimeOffset.UtcNow.AddDays(-2)));
        archive.References.Add((Films, Inside, DateTimeOffset.UtcNow.AddDays(-9)));
        var (api, _, _) = Host(archive);

        Assert.Equal("Talks", Json(await api.Video(Prepared, default)).GetProperty("channel").GetProperty("displayName").GetString());
        Assert.Equal("Talks", Json(await api.Video(Inside, default)).GetProperty("channel").GetProperty("displayName").GetString());
    }

    [Fact]
    public async Task A_video_both_in_and_referenced_from_one_channel_is_one_entry_and_a_draft_none()
    {
        var archive = Archive();
        archive.References.Add((Talks, Inside, DateTimeOffset.UtcNow));
        archive.References.Add((Talks, Draft, DateTimeOffset.UtcNow));
        var (api, _, _) = Host(archive);

        var talks = Entries(Json(await api.VideoChannelVideos(PeerTubeShapes.ChannelName(Talks), 0, 15, default)));
        Assert.Equal([(Inside.ToString("D"), "Talks")], talks);
    }

    [Fact]
    public async Task Channels_are_the_folders_wearing_the_mask_empty_ones_too_and_a_plain_folder_is_none()
    {
        var (api, _, _) = Host(Archive());
        var account = PeerTubeShapes.AccountName(Tenant);

        var channels = Json(await api.AccountChannels(account, default)).GetProperty("data").EnumerateArray().ToList();
        Assert.Equal(["Films", "Talks"], channels.Select(c => c.GetProperty("displayName").GetString()).Order());
        Assert.Equal("What was said.", channels.Single(c => c.GetProperty("displayName").GetString() == "Talks").GetProperty("description").GetString());
        Assert.IsType<NotFoundResult>(await api.VideoChannel(PeerTubeShapes.ChannelName(Elsewhere), default));
        Assert.Equal(1, Json(await api.SearchChannels("fil", default)).GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task A_tenant_that_encrypts_at_rest_has_no_channels()
    {
        var (api, _, _) = Host(Archive(), encrypts: true);
        Assert.Equal(0, Json(await api.AccountChannels(PeerTubeShapes.AccountName(Tenant), default)).GetProperty("total").GetInt32());
        Assert.Equal(0, Json(await api.Videos(0, 15, default)).GetProperty("total").GetInt32());
    }

    [Theory]
    [InlineData("mov", "video/quicktime")]
    [InlineData("ogv", "video/ogg")]
    public async Task The_type_comes_from_what_is_stored_never_from_the_name(string stored, string announced)
    {
        var archive = Archive();
        archive.References.Add((Talks, Prepared, DateTimeOffset.UtcNow));
        archive.StoredAs[Prepared] = stored;
        var (api, door, _) = Host(archive);

        var file = Json(await api.Video(Prepared, default)).GetProperty("files")[0].GetProperty("fileUrl").GetString();
        Assert.Equal($"https://video.example.test/files/{Prepared:D}.{stored}", file);
        var link = Assert.IsType<RedirectResult>(await door.Video(Prepared, stored, default)).Url;
        Assert.Contains(Uri.EscapeDataString(announced), link, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_thumbnail_door_answers_the_stored_image_type()
    {
        var archive = Archive();
        archive.References.Add((Talks, Prepared, DateTimeOffset.UtcNow));
        archive.StoredAs[Photo] = "png";
        var (_, door, _) = Host(archive);

        var link = Assert.IsType<RedirectResult>(await door.Thumbnail(Prepared, default)).Url;
        Assert.Contains(Uri.EscapeDataString("image/png"), link, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_app_finds_no_comments_and_its_view_is_acknowledged_for_a_video_it_may_see()
    {
        var (api, _, _) = Host(Archive());

        Assert.Equal(0, Json(await api.CommentThreads(Inside, default)).GetProperty("total").GetInt32());
        Assert.IsType<NoContentResult>(await api.View(Inside, default));
        Assert.IsType<NotFoundResult>(await api.CommentThreads(Draft, default));
        Assert.IsType<NotFoundResult>(await api.View(Draft, default));
    }

    [Fact]
    public void The_masks_say_what_they_hold()
    {
        Assert.True(TubeChannel.Mask.IsFolderMask);
        Assert.True(TubeChannel.Mask.AdmitsOnlyDeclaredChildren);
        Assert.True(TubeChannel.Mask.ReferencesObeyAdmission);
        Assert.Equal([Video.MaskId, VideoThumbnail.MaskId], TubeChannel.Mask.AdmittedChildren!);
        Assert.Null(Video.Mask.AllowedParents);
        Assert.Null(VideoThumbnail.Mask.AllowedParents);
        Assert.Equal(["mp4", "m4v", "webm", "mov", "mkv", "ogv"], Video.Mask.AcceptedExtensions!);
        Assert.Equal(["jpg", "jpeg", "png", "webp"], VideoThumbnail.Mask.AcceptedExtensions!);
        Assert.Contains(Video.Mask.Fields, f => f.Name == Video.Thumbnail && f.DataType == "DocumentReference");
    }
}
