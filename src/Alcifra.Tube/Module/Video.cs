using System.Globalization;
using SimplArchive.ModuleAbi;

namespace Alcifra.Tube.Module;

/// <summary>
/// The Video mask (ADR 0002): a video is a document wearing it, in any folder, its file under the module's own key
/// segment. <i>Published</i> is the publish switch, an index edit; <i>Published at</i> orders the catalogue and is the
/// editor's to fill (the core's projection hook may not write, so nothing stamps it).
/// </summary>
public static class Video
{
    public static readonly Guid MaskId = Guid.Parse("7B3E0000-0000-4000-8000-000000000001");
    public const string Segment = "tube";

    public const string Description = "Description";
    public const string Published = "Published";
    public const string PublishedAt = "Published at";
    public const string Language = "Language";
    public const string Thumbnail = "Thumbnail";
    public const string DurationSeconds = "Duration seconds";
    public const string Tags = "Tags";

    public static readonly ModuleMaskSeed Mask = new(MaskId, "Video", IsFolderMask: false, IsBookable: false,
    [
        new ModuleFieldSeed(Description, "Text"),
        new ModuleFieldSeed(Published, "Text"),
        new ModuleFieldSeed(PublishedAt, "DateTime"),
        new ModuleFieldSeed(Language, "Text"),
        new ModuleFieldSeed(Thumbnail, "Text"),
        new ModuleFieldSeed(DurationSeconds, "Number"),
        new ModuleFieldSeed(Tags, "Text", IsList: true),
    ])
    {
        ContentSegment = Segment,
    };

    /// <summary>A document's fields as a video; null when it does not wear the mask.</summary>
    public static PublishedVideo? Read(ModuleDocument document)
    {
        if (document.MaskId != MaskId)
        {
            return null;
        }

        var published = document.Fields.GetValueOrDefault(Published)?.Trim().ToLowerInvariant() is "yes" or "true" or "on" or "1";
        var publishedAt = DateTimeOffset.TryParse(document.Fields.GetValueOrDefault(PublishedAt), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var at) ? at : (DateTimeOffset?)null;
        var thumbnail = Guid.TryParse(document.Fields.GetValueOrDefault(Thumbnail), out var t) ? t : (Guid?)null;
        var duration = int.TryParse(document.Fields.GetValueOrDefault(DurationSeconds), NumberStyles.Integer, CultureInfo.InvariantCulture, out var d) && d >= 0 ? d : 0;
        return new PublishedVideo(document.Id, document.ParentId, document.Name, document.Fields.GetValueOrDefault(Description) ?? string.Empty, published, publishedAt,
            document.Fields.GetValueOrDefault(Language)?.Trim() is { Length: > 0 } l ? l : null, thumbnail, duration,
            document.Fields.GetValueOrDefault(Tags)?.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) ?? []);
    }
}

/// <summary>A video as the catalogue and the API read it: a document's fields, nothing the module keeps itself.</summary>
public sealed record PublishedVideo(Guid Id, Guid? FolderId, string Name, string Description, bool Published, DateTimeOffset? PublishedAt, string? Language, Guid? ThumbnailId, int DurationSeconds, IReadOnlyList<string> Tags);
