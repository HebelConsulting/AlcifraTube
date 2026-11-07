using Alcifra.Tube.Catalogue;
using Alcifra.Tube.Module;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Alcifra.ModuleAbi;

namespace Alcifra.Tube.Api;

/// <summary>
/// The PeerTube API's read side (ADR 0004), the paths NewPipe's extractor and browser clients request: the instance
/// config, the catalogue under every sort the clients know (all newest first: no counters here), a video with its one
/// file, its description, captions, chapters and storyboards (empty), channels (folders) and the account (the tenant)
/// with their videos, and search. Served to anyone on the tenant's video host; only published videos exist.
/// </summary>
[ApiController]
[AllowAnonymous]
[Route(TubeModule.RootPrefix + "/api/v1")]
public sealed class PeerTubeController(VideoCatalogue catalogue, VideoDirectory directory, IModuleCallerContext caller) : ControllerBase
{
    private static readonly object NoItems = new { total = 0, data = Array.Empty<object>() };

    [HttpGet("config")]
    public async Task<IActionResult> Config(CancellationToken ct)
    {
        var account = await AccountAsync(ct);
        return Ok(PeerTubeShapes.Config(account.Name, account.Description));
    }

    [HttpHead("config")]
    public Task<IActionResult> ConfigHead(CancellationToken ct) => Config(ct);

    [HttpGet("config/about")]
    public async Task<IActionResult> About(CancellationToken ct)
    {
        var account = await AccountAsync(ct);
        return Ok(PeerTubeShapes.About(account.Name, account.Description));
    }

    [HttpHead("config/about")]
    public Task<IActionResult> AboutHead(CancellationToken ct) => About(ct);

    [HttpGet("server/stats")]
    public async Task<IActionResult> Stats(CancellationToken ct) => Ok(PeerTubeShapes.Stats((await catalogue.PublishedAsync(ct)).Count));

    [HttpHead("server/stats")]
    public Task<IActionResult> StatsHead(CancellationToken ct) => Stats(ct);

    [HttpGet("videos")]
    public Task<IActionResult> Videos([FromQuery] int start = 0, [FromQuery] int count = 15, CancellationToken ct = default) => ListAsync(null, null, start, count, ct);

    [HttpHead("videos")]
    public Task<IActionResult> VideosHead([FromQuery] int start = 0, [FromQuery] int count = 15, CancellationToken ct = default) => Videos(start, count, ct);

    [HttpGet("videos/{videoId:guid}")]
    public async Task<IActionResult> Video(Guid videoId, CancellationToken ct)
    {
        if (await catalogue.PublishedAsync(videoId, ct) is not { } video)
        {
            return NotFound();
        }

        var account = await AccountAsync(ct);
        return Ok(PeerTubeShapes.VideoDetail(video, await ChannelAsync(video, ct), account, Host, await directory.FileExtensionAsync(videoId, ct)));
    }

    [HttpHead("videos/{videoId:guid}")]
    public Task<IActionResult> VideoHead(Guid videoId, CancellationToken ct) => Video(videoId, ct);

    [HttpGet("videos/{videoId:guid}/description")]
    public async Task<IActionResult> Description(Guid videoId, CancellationToken ct) =>
        await catalogue.PublishedAsync(videoId, ct) is { } video ? Ok(new { description = video.Description }) : NotFound();

    [HttpHead("videos/{videoId:guid}/description")]
    public Task<IActionResult> DescriptionHead(Guid videoId, CancellationToken ct) => Description(videoId, ct);

    // The official app asks for both on every video it opens. No comments here, and views are not counted (ADR 0004).
    [HttpGet("videos/{videoId:guid}/comment-threads")]
    public async Task<IActionResult> CommentThreads(Guid videoId, CancellationToken ct) => await catalogue.PublishedAsync(videoId, ct) is null ? NotFound() : Ok(NoItems);

    [HttpHead("videos/{videoId:guid}/comment-threads")]
    public Task<IActionResult> CommentThreadsHead(Guid videoId, CancellationToken ct) => CommentThreads(videoId, ct);

    [HttpPost("videos/{videoId:guid}/views")]
    public async Task<IActionResult> View(Guid videoId, CancellationToken ct) => await catalogue.PublishedAsync(videoId, ct) is null ? NotFound() : NoContent();

    [HttpGet("videos/{videoId:guid}/captions")]
    public async Task<IActionResult> Captions(Guid videoId, CancellationToken ct) => await catalogue.PublishedAsync(videoId, ct) is null ? NotFound() : Ok(NoItems);

    [HttpHead("videos/{videoId:guid}/captions")]
    public Task<IActionResult> CaptionsHead(Guid videoId, CancellationToken ct) => Captions(videoId, ct);

    [HttpGet("videos/{videoId:guid}/chapters")]
    public async Task<IActionResult> Chapters(Guid videoId, CancellationToken ct) => await catalogue.PublishedAsync(videoId, ct) is null ? NotFound() : Ok(new { chapters = Array.Empty<object>() });

    [HttpHead("videos/{videoId:guid}/chapters")]
    public Task<IActionResult> ChaptersHead(Guid videoId, CancellationToken ct) => Chapters(videoId, ct);

    [HttpGet("videos/{videoId:guid}/storyboards")]
    public async Task<IActionResult> Storyboards(Guid videoId, CancellationToken ct) => await catalogue.PublishedAsync(videoId, ct) is null ? NotFound() : Ok(new { storyboards = Array.Empty<object>() });

    [HttpHead("videos/{videoId:guid}/storyboards")]
    public Task<IActionResult> StoryboardsHead(Guid videoId, CancellationToken ct) => Storyboards(videoId, ct);

    [HttpGet("video-channels/{name}")]
    public async Task<IActionResult> VideoChannel(string name, CancellationToken ct) =>
        await FolderAsync(name, ct) is { } channel ? Ok(PeerTubeShapes.ChannelJson(channel, await AccountAsync(ct), Host)) : NotFound();

    [HttpHead("video-channels/{name}")]
    public Task<IActionResult> VideoChannelHead(string name, CancellationToken ct) => VideoChannel(name, ct);

    [HttpGet("video-channels/{name}/videos")]
    public async Task<IActionResult> VideoChannelVideos(string name, [FromQuery] int start = 0, [FromQuery] int count = 15, CancellationToken ct = default) =>
        await FolderAsync(name, ct) is { } channel ? await ListAsync(channel.FolderId, null, start, count, ct) : NotFound();

    [HttpHead("video-channels/{name}/videos")]
    public Task<IActionResult> VideoChannelVideosHead(string name, [FromQuery] int start = 0, [FromQuery] int count = 15, CancellationToken ct = default) => VideoChannelVideos(name, start, count, ct);

    [HttpGet("video-channels/{name}/video-playlists")]
    public async Task<IActionResult> VideoChannelPlaylists(string name, CancellationToken ct)
    {
        if (await FolderAsync(name, ct) is not { } channel)
        {
            return NotFound();
        }

        var items = new List<object>();
        foreach (var playlist in await catalogue.PlaylistsAsync(channel.FolderId, ct))
        {
            items.Add(await PlaylistJsonAsync(playlist, channel, ct));
        }

        return Ok(PeerTubeShapes.Page(items, 0, items.Count));
    }

    [HttpHead("video-channels/{name}/video-playlists")]
    public Task<IActionResult> VideoChannelPlaylistsHead(string name, CancellationToken ct) => VideoChannelPlaylists(name, ct);

    [HttpGet("video-playlists/{playlistId:guid}")]
    public async Task<IActionResult> PlaylistInfo(Guid playlistId, CancellationToken ct) =>
        await catalogue.PlaylistAsync(playlistId, ct) is { } found
            ? Ok(await PlaylistJsonAsync(found.Playlist, VideoDirectory.ChannelOf(found.Channel), ct))
            : NotFound();

    [HttpHead("video-playlists/{playlistId:guid}")]
    public Task<IActionResult> PlaylistInfoHead(Guid playlistId, CancellationToken ct) => PlaylistInfo(playlistId, ct);

    [HttpGet("video-playlists/{playlistId:guid}/videos")]
    public async Task<IActionResult> PlaylistVideos(Guid playlistId, [FromQuery] int start = 0, [FromQuery] int count = 15, CancellationToken ct = default)
    {
        if (await catalogue.PlaylistAsync(playlistId, ct) is null)
        {
            return NotFound();
        }

        var account = await AccountAsync(ct);
        var items = new List<object>();
        foreach (var video in await catalogue.PlaylistVideosAsync(playlistId, ct))
        {
            items.Add(new { id = video.Id.GetHashCode() & 0x7fffffff, position = items.Count + 1, startTimestamp = (int?)null, stopTimestamp = (int?)null, video = PeerTubeShapes.VideoItem(video, await ChannelAsync(video, ct), account, Host) });
        }

        return Ok(PeerTubeShapes.Page(items, start, count));
    }

    [HttpHead("video-playlists/{playlistId:guid}/videos")]
    public Task<IActionResult> PlaylistVideosHead(Guid playlistId, [FromQuery] int start = 0, [FromQuery] int count = 15, CancellationToken ct = default) => PlaylistVideos(playlistId, start, count, ct);

    [HttpGet("accounts/{name}")]
    public async Task<IActionResult> AccountInfo(string name, CancellationToken ct) =>
        IsOurAccount(name) ? Ok(PeerTubeShapes.AccountJson(await AccountAsync(ct), Host)) : NotFound();

    [HttpHead("accounts/{name}")]
    public Task<IActionResult> AccountInfoHead(string name, CancellationToken ct) => AccountInfo(name, ct);

    [HttpGet("accounts/{name}/videos")]
    public Task<IActionResult> AccountVideos(string name, [FromQuery] int start = 0, [FromQuery] int count = 15, CancellationToken ct = default) =>
        IsOurAccount(name) ? ListAsync(null, null, start, count, ct) : Task.FromResult<IActionResult>(NotFound());

    [HttpHead("accounts/{name}/videos")]
    public Task<IActionResult> AccountVideosHead(string name, [FromQuery] int start = 0, [FromQuery] int count = 15, CancellationToken ct = default) => AccountVideos(name, start, count, ct);

    [HttpGet("accounts/{name}/video-channels")]
    public async Task<IActionResult> AccountChannels(string name, CancellationToken ct)
    {
        if (!IsOurAccount(name))
        {
            return NotFound();
        }

        var account = await AccountAsync(ct);
        var channels = new List<object>();
        foreach (var channel in await catalogue.ChannelsAsync(ct))
        {
            channels.Add(PeerTubeShapes.ChannelJson(VideoDirectory.ChannelOf(channel), account, Host));
        }

        return Ok(new { total = channels.Count, data = channels });
    }

    [HttpHead("accounts/{name}/video-channels")]
    public Task<IActionResult> AccountChannelsHead(string name, CancellationToken ct) => AccountChannels(name, ct);

    [HttpGet("search/videos")]
    public Task<IActionResult> SearchVideos([FromQuery] string? search, [FromQuery] int start = 0, [FromQuery] int count = 15, CancellationToken ct = default) =>
        ListAsync(null, search, start, count, ct);

    [HttpHead("search/videos")]
    public Task<IActionResult> SearchVideosHead([FromQuery] string? search, [FromQuery] int start = 0, [FromQuery] int count = 15, CancellationToken ct = default) => SearchVideos(search, start, count, ct);

    [HttpGet("search/video-channels")]
    public async Task<IActionResult> SearchChannels([FromQuery] string? search, CancellationToken ct)
    {
        var account = await AccountAsync(ct);
        var channels = new List<object>();
        foreach (var channel in (await catalogue.ChannelsAsync(ct)).Select(VideoDirectory.ChannelOf))
        {
            if (string.IsNullOrWhiteSpace(search) || channel.Name.Contains(search, StringComparison.OrdinalIgnoreCase))
            {
                channels.Add(PeerTubeShapes.ChannelJson(channel, account, Host));
            }
        }

        return Ok(new { total = channels.Count, data = channels });
    }

    [HttpHead("search/video-channels")]
    public Task<IActionResult> SearchChannelsHead([FromQuery] string? search, CancellationToken ct) => SearchChannels(search, ct);

    [HttpGet("search/video-playlists")]
    public async Task<IActionResult> SearchPlaylists([FromQuery] string? search, CancellationToken ct)
    {
        var found = new List<object>();
        foreach (var channel in await catalogue.ChannelsAsync(ct))
        {
            foreach (var playlist in await catalogue.PlaylistsAsync(channel.Id, ct))
            {
                if (string.IsNullOrWhiteSpace(search) || playlist.Name.Contains(search, StringComparison.OrdinalIgnoreCase))
                {
                    found.Add(await PlaylistJsonAsync(playlist, VideoDirectory.ChannelOf(channel), ct));
                }
            }
        }

        return Ok(PeerTubeShapes.Page(found, 0, found.Count));
    }

    [HttpHead("search/video-playlists")]
    public Task<IActionResult> SearchPlaylistsHead([FromQuery] string? search, CancellationToken ct) => SearchPlaylists(search, ct);

    // The catalogue, optionally one channel's and optionally searched; every sort the clients know is newest first.
    private async Task<IActionResult> ListAsync(Guid? folderId, string? search, int start, int count, CancellationToken ct)
    {
        var account = await AccountAsync(ct);
        var videos = (await catalogue.PublishedAsync(ct))
            .Where(v => folderId is null || v.FolderId == folderId)
            .Where(v => string.IsNullOrWhiteSpace(search)
                || v.Name.Contains(search, StringComparison.OrdinalIgnoreCase)
                || v.Description.Contains(search, StringComparison.OrdinalIgnoreCase)
                || v.Tags.Any(t => t.Contains(search, StringComparison.OrdinalIgnoreCase)))
            .ToList();
        var items = new List<object>();
        foreach (var video in videos)
        {
            items.Add(PeerTubeShapes.VideoItem(video, await ChannelAsync(video, ct), account, Host));
        }

        return Ok(PeerTubeShapes.Page(items, start, count));
    }

    private string Host => Request.Host.Host;

    private async Task<object> PlaylistJsonAsync(ModuleDocument playlist, Channel channel, CancellationToken ct) =>
        PeerTubeShapes.PlaylistJson(playlist.Id, playlist.Name, (await catalogue.PlaylistVideosAsync(playlist.Id, ct)).Count, channel, await AccountAsync(ct), Host);

    private bool IsOurAccount(string name) => name.Split('@')[0] == PeerTubeShapes.AccountName(caller.TenantId);

    private Task<Account> AccountAsync(CancellationToken ct) => directory.AccountAsync(Host, ct);

    private Task<Channel> ChannelAsync(PublishedVideo video, CancellationToken ct) => directory.ChannelAsync(video, ct);

    private async Task<Channel?> FolderAsync(string name, CancellationToken ct)
    {
        var bare = name.Split('@')[0];
        if (!bare.StartsWith("f-", StringComparison.Ordinal) || !Guid.TryParseExact(bare[2..], "N", out var folderId))
        {
            return null;
        }

        return (await catalogue.ChannelsAsync(ct)).FirstOrDefault(c => c.Id == folderId) is { } channel ? VideoDirectory.ChannelOf(channel) : null;
    }
}
