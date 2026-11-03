using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Alcifra.Tube.Module;

namespace Alcifra.Tube.Api;

/// <summary>
/// The PeerTube JSON the clients read (ADR 0004), shaped from a video document, its folder and the tenant: the property
/// names are PeerTube's, the ids ours (<c>uuid</c> = the document id, a channel's name <c>f-&lt;folder id&gt;</c>, the
/// account's <c>t-&lt;tenant id&gt;</c>), every address absolute on the video host. What the clients read and we cannot
/// fill (views, likes) is 0; what we do not do (live, NSFW, playlists) is false or empty.
/// </summary>
public static class PeerTubeShapes
{
    public const int PrivacyPublic = 1;

    public static string ChannelName(Guid folderId) => $"f-{folderId:N}";
    public static string AccountName(Guid tenantId) => $"t-{tenantId:N}";

    public static object Config(string name, string description) => new
    {
        instance = new { name, shortDescription = description, description, isNSFW = false, defaultNSFWPolicy = "do_not_list", customizations = new { javascript = string.Empty, css = string.Empty } },
        serverVersion = "6.0.0-alcifra",
        plugin = new { registered = Array.Empty<object>() },
        theme = new { registered = Array.Empty<object>(), @default = "default" },
        transcoding = new { enabledResolutions = Array.Empty<int>(), hls = new { enabled = false }, webVideos = new { enabled = false } },
        tracker = new { enabled = false },
        search = new { remoteUri = new { users = false, anonymous = false }, searchIndex = new { enabled = false } },
        signup = new { allowed = false },
        import_ = new { videos = new { http = new { enabled = false }, torrent = new { enabled = false } } },
    };

    public static object VideoItem(PublishedVideo video, Channel channel, Account account, string host)
    {
        var origin = $"https://{host}";
        return new
        {
            id = video.Id.GetHashCode() & 0x7fffffff,
            uuid = video.Id.ToString("D"),
            shortUUID = video.Id.ToString("N"),
            url = $"{origin}/videos/watch/{video.Id:D}",
            name = video.Name,
            category = new { id = (int?)null, label = "Unknown" },
            licence = new { id = (int?)null, label = "Unknown" },
            language = new { id = video.Language, label = video.Language ?? "Unknown" },
            privacy = new { id = PrivacyPublic, label = "Public" },
            nsfw = false,
            description = Truncate(video.Description, 250),
            isLocal = true,
            duration = video.DurationSeconds,
            views = 0,
            likes = 0,
            dislikes = 0,
            thumbnailPath = video.ThumbnailId is null ? null : $"/files/{video.Id:D}/thumbnail",
            previewPath = video.ThumbnailId is null ? null : $"/files/{video.Id:D}/thumbnail",
            embedPath = $"/videos/embed/{video.Id:D}",
            createdAt = Stamp(video.PublishedAt),
            updatedAt = Stamp(video.PublishedAt),
            publishedAt = Stamp(video.PublishedAt),
            originallyPublishedAt = (string?)null,
            isLive = false,
            account = AccountJson(account, host),
            channel = ChannelJson(channel, account, host),
        };
    }

    /// <summary>
    /// A video's details are its list item's fields and the detail-only ones in ONE object, as PeerTube's VideoDetails
    /// extends Video: a client reads <c>name</c> and <c>channel</c> beside <c>files</c>. Nested under a property of their
    /// own, NewPipe found no name and played nothing (kiosk, 2026-11-03).
    /// </summary>
    public static JsonObject VideoDetail(PublishedVideo video, Channel channel, Account account, string host, string extension)
    {
        var detail = JsonSerializer.SerializeToNode(VideoItem(video, channel, account, host))!.AsObject();
        foreach (var (name, value) in JsonSerializer.SerializeToNode(DetailOnly(video, host, extension))!.AsObject().ToList())
        {
            detail[name] = value?.DeepClone();
        }

        return detail;
    }

    private static object DetailOnly(PublishedVideo video, string host, string extension)
    {
        var origin = $"https://{host}";
        return new
        {
            description = video.Description,
            support = (string?)null,
            tags = video.Tags,
            commentsEnabled = false,
            downloadEnabled = true,
            waitTranscoding = false,
            state = new { id = 1, label = "Published" },
            trackerUrls = Array.Empty<string>(),
            files = new[]
            {
                new
                {
                    id = 1,
                    resolution = new { id = 0, label = "Original" },
                    magnetUri = string.Empty,
                    size = 0,
                    torrentUrl = string.Empty,
                    torrentDownloadUrl = string.Empty,
                    fileUrl = $"{origin}/files/{video.Id:D}.{extension}",
                    fileDownloadUrl = $"{origin}/files/{video.Id:D}.{extension}",
                    fps = -1,
                    hasAudio = true,
                    hasVideo = true,
                },
            },
            streamingPlaylists = Array.Empty<object>(),
        };
    }

    public static object ChannelJson(Channel channel, Account account, string host)
    {
        var origin = $"https://{host}";
        return new
        {
            id = channel.FolderId.GetHashCode() & 0x7fffffff,
            name = ChannelName(channel.FolderId),
            displayName = channel.Name,
            description = (string?)null,
            support = (string?)null,
            url = $"{origin}/video-channels/{ChannelName(channel.FolderId)}",
            host,
            isLocal = true,
            followersCount = 0,
            followingCount = 0,
            avatars = Array.Empty<object>(),
            banners = Array.Empty<object>(),
            ownerAccount = AccountJson(account, host),
        };
    }

    public static object AccountJson(Account account, string host)
    {
        var origin = $"https://{host}";
        return new
        {
            id = account.TenantId.GetHashCode() & 0x7fffffff,
            name = AccountName(account.TenantId),
            displayName = account.Name,
            description = account.Description,
            url = $"{origin}/accounts/{AccountName(account.TenantId)}",
            host,
            isLocal = true,
            followersCount = 0,
            followingCount = 0,
            avatars = Array.Empty<object>(),
            banners = Array.Empty<object>(),
        };
    }

    public static object Page<T>(IReadOnlyList<T> all, int start, int count) => new
    {
        total = all.Count,
        data = all.Skip(Math.Max(0, start)).Take(Math.Clamp(count, 1, 100)).ToList(),
    };

    private static string? Stamp(DateTimeOffset? at) => at?.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);

    private static string Truncate(string text, int max) => text.Length <= max ? text : text[..max];
}

/// <summary>A channel is a folder (ADR 0002).</summary>
public sealed record Channel(Guid FolderId, string Name);

/// <summary>The account is the tenant: named by its video host's settings.</summary>
public sealed record Account(Guid TenantId, string Name, string Description);
