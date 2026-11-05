using Alcifra.Tube.Api;
using Alcifra.Tube.Module;
using Alcifra.ModuleAbi;

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
    public async Task<Channel> ChannelOfAsync(Guid folderId, CancellationToken ct)
    {
        var folder = await archive.GetDocumentAsync(folderId, ct);
        return new Channel(folderId, folder?.Name ?? "Videos", Described(folder?.Fields.GetValueOrDefault(TubeChannel.Description)));
    }

    /// <summary>A channel as the API shows it.</summary>
    public static Channel ChannelOf(ModuleDocument channel) =>
        new(channel.Id, channel.Name, Described(channel.Fields.GetValueOrDefault(TubeChannel.Description)));

    private static string? Described(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    /// <summary>
    /// The extension the file door serves the video under: what it is stored as, since a name proves nothing, else mp4.
    /// </summary>
    public async Task<string> FileExtensionAsync(Guid videoId, CancellationToken ct)
    {
        var extension = await archive.GetDocumentContentExtensionAsync(videoId, ct) ?? string.Empty;
        return Doors.FilesController.ContentTypeOf(extension) is null ? "mp4" : extension;
    }
}
