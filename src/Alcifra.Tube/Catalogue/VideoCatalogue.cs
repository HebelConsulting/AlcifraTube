using Alcifra.Tube.Module;
using Microsoft.Extensions.Logging;
using Alcifra.ModuleAbi;

namespace Alcifra.Tube.Catalogue;

/// <summary>
/// The published videos of the tenant the request was resolved for (ADR 0006): what its channels hold or reference, filtered
/// on <i>Published</i>, newest first. A video outside every channel is offered nowhere, whatever its field says. The list has
/// <b>one entry per channel a video is in</b>, each naming that channel. On a tenant that encrypts at rest it is empty and says
/// why once an hour (ADR 0003): a presigned link there would lead to the decrypting door.
/// </summary>
public sealed class VideoCatalogue(IModuleArchiveFacade archive, IModuleCallerContext caller, EncryptionWarnings warnings, ILogger<VideoCatalogue> logger)
{
    /// <summary>The channels this module can see; none on a tenant that encrypts at rest.</summary>
    public async Task<IReadOnlyList<ModuleDocument>> ChannelsAsync(CancellationToken cancellationToken)
    {
        if (await caller.TenantEncryptsAtRestAsync(cancellationToken))
        {
            warnings.Say(caller.TenantId, logger);
            return [];
        }

        return await archive.GetByMaskAsync(TubeChannel.MaskId, cancellationToken);
    }

    /// <summary>Every published video, once per channel it is in, each carrying that channel.</summary>
    public async Task<IReadOnlyList<PublishedVideo>> PublishedAsync(CancellationToken cancellationToken) =>
        [.. (await PlacementsAsync(cancellationToken))
            .Select(p => p.Video)
            .OrderByDescending(v => v.PublishedAt ?? DateTimeOffset.MinValue)
            .ThenBy(v => v.Name, StringComparer.Ordinal)
            .ThenBy(v => v.FolderId)];

    /// <summary>
    /// One published video, or null: an unpublished one, or one in no channel, is 404 on its next request. A request by id
    /// names no channel, so this names one fixed: where the video sits, else its oldest reference.
    /// </summary>
    public async Task<PublishedVideo?> PublishedAsync(Guid videoId, CancellationToken cancellationToken) =>
        (await PlacementsAsync(cancellationToken))
            .Where(p => p.Video.Id == videoId)
            .OrderBy(p => p.Sits ? 0 : 1)
            .ThenBy(p => p.ReferencedAt ?? DateTimeOffset.MaxValue)
            .ThenBy(p => p.Video.FolderId)
            .Select(p => p.Video)
            .FirstOrDefault();

    private async Task<List<Placement>> PlacementsAsync(CancellationToken cancellationToken)
    {
        var placements = new List<Placement>();
        foreach (var channel in await ChannelsAsync(cancellationToken))
        {
            var sitting = await archive.GetChildrenAsync(channel.Id, Video.MaskId, cancellationToken);
            var referenced = await archive.GetReferencedAsync(channel.Id, Video.MaskId, cancellationToken);
            foreach (var document in sitting.Concat(referenced).DistinctBy(d => d.Id))
            {
                if (Video.Read(document) is { Published: true } video)
                {
                    placements.Add(new Placement(video with { FolderId = channel.Id }, document.ParentId == channel.Id, document.ReferencedAt));
                }
            }
        }

        return placements;
    }

    private sealed record Placement(PublishedVideo Video, bool Sits, DateTimeOffset? ReferencedAt);
}

/// <summary>One Warning per tenant per hour, not one per request: a tenant that encrypts at rest asks for it once.</summary>
public sealed class EncryptionWarnings
{
    private readonly Dictionary<Guid, DateTimeOffset> _said = [];

    public void Say(Guid tenantId, ILogger logger)
    {
        lock (_said)
        {
            if (_said.TryGetValue(tenantId, out var last) && last > DateTimeOffset.UtcNow.AddHours(-1))
            {
                return;
            }

            _said[tenantId] = DateTimeOffset.UtcNow;
        }

        logger.LogWarning("Tenant {TenantId} encrypts content at rest: its videos are not served, since a presigned link would carry the bytes through the server. Switch at-rest encryption off for this tenant to serve them.", tenantId);
    }
}
