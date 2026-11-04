using Alcifra.Tube.Api;
using Alcifra.Tube.Module;
using SimplArchive.ModuleAbi;

namespace Alcifra.Tube.Catalogue;

/// <summary>
/// The channels as one signed-in person may see them (ADR 0007), unlike the public answers: their own rights decide, asked
/// of the core per document, and someone who may edit a video also sees it unpublished. Changes do not come through here;
/// the clients make them through the core's own rels, so the same rights and refusals apply.
/// </summary>
public sealed class ChannelShelf(IModuleArchiveFacade archive, IModuleDocumentRights rights, IModuleCallerContext caller, VideoCatalogue catalogue)
{
    public async Task<ChannelShelfResource> ReadAsync(CancellationToken cancellationToken)
    {
        if (await caller.TenantEncryptsAtRestAsync(cancellationToken))
        {
            return new ChannelShelfResource { EncryptsAtRest = true };
        }

        var host = (await archive.GetSettingAsync(TubeSettings.HostKey, cancellationToken))?.Trim();
        var shelf = new ChannelShelfResource();
        foreach (var channel in (await catalogue.ChannelsAsync(cancellationToken)).OrderBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            var onChannel = await rights.GetAsync(channel.Id, cancellationToken);
            if (onChannel.CanSee)
            {
                shelf.Items.Add(new ChannelShelfItem
                {
                    Id = channel.Id,
                    Name = channel.Name,
                    Description = VideoDirectory.ChannelOf(channel).Description,
                    CanWrite = onChannel.CanCreateSubItems,
                    Videos = await VideosAsync(channel.Id, onChannel, host, cancellationToken),
                });
            }
        }

        return shelf;
    }

    private async Task<List<ChannelShelfVideo>> VideosAsync(Guid channelId, ModuleDocumentRightsAnswer onChannel, string? host, CancellationToken cancellationToken)
    {
        var sitting = await archive.GetChildrenAsync(channelId, Video.MaskId, cancellationToken);
        var referenced = await archive.GetReferencedAsync(channelId, Video.MaskId, cancellationToken);
        var videos = new List<ChannelShelfVideo>();
        foreach (var document in sitting.Concat(referenced).DistinctBy(d => d.Id))
        {
            var video = Video.Read(document)!;
            var onVideo = await rights.GetAsync(document.Id, cancellationToken);
            if (onVideo.CanSee && (video.Published || onVideo.CanEditIndexData))
            {
                var isReference = document.ParentId != channelId;
                videos.Add(new ChannelShelfVideo
                {
                    Id = video.Id,
                    Name = video.Name,
                    Description = video.Description,
                    Tags = [.. video.Tags],
                    Published = video.Published,
                    PublishedAt = video.PublishedAt,
                    DurationSeconds = video.DurationSeconds,
                    ThumbnailId = video.ThumbnailId,
                    IsReference = isReference,
                    CanEdit = onVideo.CanEditIndexData,
                    CanPlay = onVideo.CanReadContent,
                    CanRemoveFromChannel = isReference && onChannel.CanCreateSubItems,
                    WatchUrl = video.Published && !string.IsNullOrEmpty(host) ? $"https://{host}/videos/watch/{video.Id:D}" : null,
                });
            }
        }

        return [.. videos.OrderByDescending(v => v.PublishedAt ?? DateTimeOffset.MinValue).ThenBy(v => v.Name, StringComparer.Ordinal)];
    }
}
