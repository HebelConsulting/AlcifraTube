using SimplArchive.ModuleAbi;

namespace Alcifra.Tube.Api;

/// <summary>The Tube tab's answer (ADR 0007): the channels a person can see, what is in them for that person, and why it may be empty.</summary>
public sealed class ChannelShelfResource : HypermediaResource
{
    /// <summary>True on a tenant that encrypts at rest, where the list is empty and no video is served (ADR 0003).</summary>
    public bool EncryptsAtRest { get; set; }

    public List<ChannelShelfItem> Items { get; set; } = [];
}

/// <summary>One channel. <see cref="CanWrite"/> false is the lock: nothing can be added to it or removed from it.</summary>
public sealed class ChannelShelfItem
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool CanWrite { get; set; }
    public List<ChannelShelfVideo> Videos { get; set; } = [];
}

/// <summary>One video in one channel, with what this person may do to it; a video in two channels is listed in both.</summary>
public sealed class ChannelShelfVideo
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public List<string> Tags { get; set; } = [];
    public bool Published { get; set; }
    public DateTimeOffset? PublishedAt { get; set; }
    public int DurationSeconds { get; set; }
    public Guid? ThumbnailId { get; set; }

    /// <summary>True when the channel only references the video, which is prepared elsewhere.</summary>
    public bool IsReference { get; set; }

    public bool CanEdit { get; set; }
    public bool CanPlay { get; set; }

    /// <summary>Only a reference can be taken out of a channel, and that never deletes the video.</summary>
    public bool CanRemoveFromChannel { get; set; }

    /// <summary>The public watch page, when the video is published and the tenant named its video host.</summary>
    public string? WatchUrl { get; set; }
}
