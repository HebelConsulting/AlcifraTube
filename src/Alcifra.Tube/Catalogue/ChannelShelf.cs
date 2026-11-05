using Alcifra.Tube.Api;
using Alcifra.Tube.Module;
using Alcifra.ModuleAbi;

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
                    Links = [DocumentLink(channel.Id)],
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

    // A client changes things through the core's rels, and can follow them only from the resource (core ADR 0543): the row
    // hands it that address, so it never composes one from an id.
    private static Link DocumentLink(Guid documentId) => new("document", $"/api/documents/{documentId:D}", "GET");

    // A rel only where following it can work (ADR 0543): the file for who may read it, the picture for a video that names one.
    private static List<Link> LinksOf(PublishedVideo video, ModuleDocumentRightsAnswer onVideo)
    {
        var links = new List<Link> { DocumentLink(video.Id) };
        if (onVideo.CanReadContent)
        {
            links.Add(new Link("play", $"/{ChannelsController.Base}/videos/{video.Id:D}/play", "GET"));
        }

        if (video.ThumbnailId is not null && onVideo.CanSee)
        {
            links.Add(new Link("thumbnail", $"/{ChannelsController.Base}/videos/{video.Id:D}/thumbnail", "GET"));
        }

        return links;
    }

    /// <summary>The file's address for this person, or null: their rights decide, and only for a video in a channel they can see.</summary>
    public async Task<ContentLinkResource?> PlayAsync(Guid videoId, CancellationToken cancellationToken)
    {
        if (!(await rights.GetAsync(videoId, cancellationToken)).CanReadContent || await InShelfAsync(videoId, cancellationToken) is null)
        {
            return null;
        }

        var extension = await archive.GetDocumentContentExtensionAsync(videoId, cancellationToken) ?? string.Empty;
        return Doors.FilesController.ContentTypeOf(extension) is { } contentType
            ? await LinkAsync(videoId, contentType, cancellationToken)
            : null;
    }

    /// <summary>The picture's address, on the same terms: the person must be able to read the picture too.</summary>
    public async Task<ContentLinkResource?> ThumbnailAsync(Guid videoId, CancellationToken cancellationToken)
    {
        if (await InShelfAsync(videoId, cancellationToken) is not { ThumbnailId: { } thumbnailId }
            || !(await rights.GetAsync(thumbnailId, cancellationToken)).CanReadContent)
        {
            return null;
        }

        var extension = await archive.GetDocumentContentExtensionAsync(thumbnailId, cancellationToken) ?? string.Empty;
        return Doors.FilesController.ImageTypeOf(extension) is { } contentType
            ? await LinkAsync(thumbnailId, contentType, cancellationToken)
            : null;
    }

    private async Task<ContentLinkResource?> LinkAsync(Guid documentId, string contentType, CancellationToken cancellationToken) =>
        await archive.GetDocumentContentUrlAsync(documentId, Doors.FilesController.LinkLifetime, contentType, inline: true, cancellationToken) is { } link
            ? new ContentLinkResource { Url = link.ToString(), ContentType = contentType }
            : null;

    // A video the person sees on this tab, whichever channel holds it.
    private async Task<ChannelShelfVideo?> InShelfAsync(Guid videoId, CancellationToken cancellationToken) =>
        (await ReadAsync(cancellationToken)).Items.SelectMany(c => c.Videos).FirstOrDefault(v => v.Id == videoId);

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
                    Links = LinksOf(video, onVideo),
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
