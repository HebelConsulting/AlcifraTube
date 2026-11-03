using Alcifra.Tube.Api;
using Alcifra.Tube.Module;
using SimplArchive.ModuleAbi;

namespace Alcifra.Tube.Catalogue;

/// <summary>
/// Who and where a published video belongs to, for every surface that shows one (the API, the watch page): the account
/// is the tenant, named by its video host's settings; the channel is the video's folder (ADR 0002).
/// </summary>
public sealed class VideoDirectory(IModuleArchiveFacade archive, IModuleCallerContext caller)
{
    public async Task<Account> AccountAsync(string host, CancellationToken ct)
    {
        var name = await archive.GetSettingAsync(TubeSettings.NameKey, ct);
        var description = await archive.GetSettingAsync(TubeSettings.DescriptionKey, ct);
        return new Account(caller.TenantId, string.IsNullOrWhiteSpace(name) ? host : name.Trim(), description?.Trim() ?? string.Empty);
    }

    public Task<Channel> ChannelAsync(PublishedVideo video, CancellationToken ct) =>
        video.FolderId is { } folderId ? ChannelOfAsync(folderId, ct) : Task.FromResult(new Channel(Guid.Empty, "Videos"));

    // The folder's name when the module's principal may see it; the channel's handle still works without.
    public async Task<Channel> ChannelOfAsync(Guid folderId, CancellationToken ct) =>
        new(folderId, (await archive.GetDocumentAsync(folderId, ct))?.Name ?? "Videos");

    /// <summary>The extension the file door serves the video under: the stored one when the door knows it, else mp4.</summary>
    public async Task<string> FileExtensionAsync(Guid videoId, CancellationToken ct)
    {
        var name = (await archive.GetDocumentAsync(videoId, ct))?.Name;
        var extension = Path.GetExtension(name ?? string.Empty).TrimStart('.').ToLowerInvariant();
        return Doors.FilesController.ContentTypeOf(extension) is null ? "mp4" : extension;
    }
}
