using Alcifra.Tube.Module;
using Microsoft.Extensions.Logging;
using SimplArchive.ModuleAbi;

namespace Alcifra.Tube.Catalogue;

/// <summary>
/// The published videos of the tenant the request was resolved for (ADR 0002): the facade's listing filtered on
/// <i>Published</i>, newest first; only published videos exist to the API. On a tenant that encrypts at rest the
/// catalogue is empty and says why once an hour (ADR 0003): a presigned link there would lead to the decrypting door.
/// </summary>
public sealed class VideoCatalogue(IModuleArchiveFacade archive, IModuleCallerContext caller, EncryptionWarnings warnings, ILogger<VideoCatalogue> logger)
{
    public async Task<IReadOnlyList<PublishedVideo>> PublishedAsync(CancellationToken cancellationToken)
    {
        if (await caller.TenantEncryptsAtRestAsync(cancellationToken))
        {
            warnings.Say(caller.TenantId, logger);
            return [];
        }

        return [.. (await archive.GetByMaskAsync(Video.MaskId, cancellationToken))
            .Select(Video.Read)
            .Where(v => v is { Published: true })
            .Select(v => v!)
            .OrderByDescending(v => v.PublishedAt ?? DateTimeOffset.MinValue)
            .ThenBy(v => v.Name, StringComparer.Ordinal)];
    }

    /// <summary>One published video, or null: an unpublished one is 404 on its next request.</summary>
    public async Task<PublishedVideo?> PublishedAsync(Guid videoId, CancellationToken cancellationToken)
    {
        if (await caller.TenantEncryptsAtRestAsync(cancellationToken))
        {
            warnings.Say(caller.TenantId, logger);
            return null;
        }

        var document = await archive.GetDocumentAsync(videoId, cancellationToken);
        return document is null ? null : Video.Read(document) is { Published: true } video ? video : null;
    }
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
