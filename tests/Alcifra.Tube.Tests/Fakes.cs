using SimplArchive.ModuleAbi;

namespace Alcifra.Tube.Tests;

/// <summary>The archive as this module asks it: documents by mask and by id, settings, and the player's link; a stray call throws.</summary>
internal sealed class StubArchive(IReadOnlyList<ModuleDocument> documents, IReadOnlyDictionary<string, string> settings, Func<Guid, Uri?>? link = null) : IModuleArchiveFacade
{
    public Task<string?> GetSettingAsync(string key, CancellationToken cancellationToken = default) => Task.FromResult(settings.GetValueOrDefault(key));
    public Task<IReadOnlyList<ModuleDocument>> GetByMaskAsync(Guid maskId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<ModuleDocument>>([.. documents.Where(d => d.MaskId == maskId)]);
    public Task<ModuleDocument?> GetDocumentAsync(Guid documentId, CancellationToken cancellationToken = default) => Task.FromResult(documents.FirstOrDefault(d => d.Id == documentId));
    public Task<Uri?> GetDocumentContentUrlAsync(Guid documentId, TimeSpan ttl, string contentType, bool inline, CancellationToken cancellationToken = default) =>
        Task.FromResult(link?.Invoke(documentId) is { } u ? new Uri($"{u}?response-content-type={Uri.EscapeDataString(contentType)}&disposition={(inline ? "inline" : "attachment")}") : null);
    public Task<byte[]?> GetDocumentContentAsync(Guid documentId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<IReadOnlyList<ModuleDocument>> GetChildrenAsync(Guid parentDocumentId, Guid maskId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<ModuleDocument>>([.. documents.Where(d => d.ParentId == parentDocumentId && d.MaskId == maskId)]);

    /// <summary>What is referenced where, in the order placed.</summary>
    public List<(Guid Folder, Guid Target, DateTimeOffset At)> References { get; } = [];

    public Task<IReadOnlyList<ModuleDocument>> GetReferencedAsync(Guid folderId, Guid maskId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<ModuleDocument>>([.. References
            .Where(r => r.Folder == folderId)
            .Select(r => (Reference: r, Document: documents.SingleOrDefault(d => d.Id == r.Target && d.MaskId == maskId)))
            .Where(x => x.Document is not null)
            .Select(x => x.Document! with { ReferencedAt = x.Reference.At })]);

    /// <summary>What each document is stored as, independent of its name.</summary>
    public Dictionary<Guid, string> StoredAs { get; } = [];

    public Task<string?> GetDocumentContentExtensionAsync(Guid documentId, CancellationToken cancellationToken = default) =>
        Task.FromResult<string?>(StoredAs.GetValueOrDefault(documentId)
            ?? Path.GetExtension(documents.FirstOrDefault(d => d.Id == documentId)?.Name ?? string.Empty).TrimStart('.').ToLowerInvariant());
    public Task<Guid> CreateDocumentAsync(Guid parentDocumentId, Guid maskId, string name, IReadOnlyDictionary<string, string>? fields = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task SetFieldsAsync(Guid documentId, IReadOnlyDictionary<string, string> fields, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task SetFieldListAsync(Guid documentId, string fieldName, IReadOnlyList<string> values, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task CreateReferenceAsync(Guid targetDocumentId, Guid intoFolderId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task RenameDocumentAsync(Guid documentId, string name, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<Guid> StageContentAsync(Guid parentFolderId, Guid maskId, string name, byte[] content, string extension, DateTimeOffset expiresAt, IReadOnlyDictionary<string, string>? fields = null, Guid? replaceDocumentId = null, DateOnly? documentDate = null, TimeOnly? documentTime = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<Guid> CreateContentDocumentAsync(Guid parentFolderId, Guid maskId, string name, byte[] content, string extension, IReadOnlyDictionary<string, string>? fields = null, Guid? replaceDocumentId = null, DateOnly? documentDate = null, TimeOnly? documentTime = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task ReplaceContentAsync(Guid documentId, byte[] content, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<bool> IsOfferedAsync(Guid resourceDocumentId, DateTimeOffset startsAtUtc, DateTimeOffset endsAtUtc, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<bool> IsFreeAsync(Guid resourceDocumentId, DateTimeOffset startsAt, DateTimeOffset endsAt, CancellationToken cancellationToken = default) => throw new NotSupportedException();
}


internal sealed class StubCaller(Guid tenantId, bool encryptsAtRest = false) : IModuleCallerContext
{
    public Guid TenantId => tenantId;
    public Guid? UserId => null;
    public Guid? ServiceAccountId => Guid.NewGuid();
    public Task<bool> IsTenantAdminAsync(CancellationToken cancellationToken = default) => Task.FromResult(false);
    public Task<ModuleCallerIdentity?> GetIdentityAsync(CancellationToken cancellationToken = default) => Task.FromResult<ModuleCallerIdentity?>(null);
    public Task<bool> TenantEncryptsAtRestAsync(CancellationToken cancellationToken = default) => Task.FromResult(encryptsAtRest);
}
