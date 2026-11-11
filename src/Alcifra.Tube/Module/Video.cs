using System.Globalization;
using Alcifra.ModuleAbi;

namespace Alcifra.Tube.Module;

/// <summary>
/// The Video mask (ADR 0002, ADR 0006): a video is a document wearing it, prepared in any folder, its file under the
/// module's own key segment. It is offered only through a <see cref="TubeChannel"/>, by sitting in one or being referenced
/// from one. <i>Published</i> is the publish switch, an index edit; <i>Published at</i> orders the catalogue and is the
/// editor's to fill (the core's projection hook may not write, so nothing stamps it).
/// </summary>
public static class Video
{
    public static readonly Guid MaskId = Guid.Parse("7B3E0000-0000-4000-8000-000000000001");

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
        new ModuleFieldSeed(Published, "Toggle"),
        new ModuleFieldSeed(PublishedAt, "DateTime"),
        new ModuleFieldSeed(Language, "Text"),
        // Accepts only a Video Thumbnail (ABI 2.1, core ADR 1068): the core refuses any other target, and the clients pick
        // from thumbnails, with "From a file" filing a new one beside the video.
        new ModuleFieldSeed(Thumbnail, "DocumentReference") { AcceptedMasks = [VideoThumbnail.MaskId] },
        new ModuleFieldSeed(DurationSeconds, "Number"),
        new ModuleFieldSeed(Tags, "Text", IsList: true),
    ])
    {
        // No content segment of its own (owner, 2026-11-11): a tenant that publishes videos keeps them as the important
        // data they are, under the archive's ordinary keys, backed up and kept like every other document. Versions filed
        // under `tube/` before stay there and are read by their stored key.

        // What the file door can announce to a player. We do not transcode, so what is filed is what clients must play.
        AcceptedExtensions = ["mp4", "m4v", "webm", "mov", "mkv", "ogv"],
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

/// <summary>
/// A Tube Channel (ADR 0006): a folder advertised as one channel, so a repository can hold several. It holds videos and
/// their thumbnails; a reference placed in it obeys that too, and a published video referenced from it is in the channel.
/// </summary>
public static class TubeChannel
{
    public static readonly Guid MaskId = Guid.Parse("7B3E0000-0000-4000-8000-000000000002");
    public const string Description = "Description";

    public static readonly ModuleMaskSeed Mask = new(MaskId, "Tube Channel", IsFolderMask: true, IsBookable: false,
    [
        new ModuleFieldSeed(Description, "Text"),
    ],
    AdmitsOnlyDeclaredChildren: true,
    AdmittedChildren: [Video.MaskId, VideoThumbnail.MaskId, TubePlaylist.MaskId])
    {
        ReferencesObeyAdmission = true,
    };
}

/// <summary>
/// A playlist (ADR 0012): a folder under a channel holding only references to videos, of any channel the person can see. It
/// is published with its channel and shows only what is published in a channel.
/// </summary>
public static class TubePlaylist
{
    public static readonly Guid MaskId = Guid.Parse("7B3E0000-0000-4000-8000-000000000004");

    public static readonly ModuleMaskSeed Mask = new(MaskId, "Tube Playlist", IsFolderMask: true, IsBookable: false, [],
        AllowedParents: [TubeChannel.MaskId],
        AdmitsOnlyDeclaredChildren: true,
        AdmittedChildren: [Video.MaskId])
    {
        ReferencesObeyAdmission = true,
    };
}

/// <summary>A video's thumbnail photo (ADR 0006), prepared anywhere: the video's Thumbnail field names it, and accepts nothing
/// else (core ADR 1068).</summary>
public static class VideoThumbnail
{
    public static readonly Guid MaskId = Guid.Parse("7B3E0000-0000-4000-8000-000000000003");

    public static readonly ModuleMaskSeed Mask = new(MaskId, "Video Thumbnail", IsFolderMask: false, IsBookable: false, [])
    {
        AcceptedExtensions = ["jpg", "jpeg", "png", "webp"],
    };
}

/// <summary>A video as the catalogue and the API read it: a document's fields, nothing the module keeps itself.</summary>
public sealed record PublishedVideo(Guid Id, Guid? FolderId, string Name, string Description, bool Published, DateTimeOffset? PublishedAt, string? Language, Guid? ThumbnailId, int DurationSeconds, IReadOnlyList<string> Tags);
