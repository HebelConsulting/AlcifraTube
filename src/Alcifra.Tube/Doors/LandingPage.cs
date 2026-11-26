using Alcifra.ModuleAbi;
using Alcifra.Tube.Api;
using Alcifra.Tube.Module;

namespace Alcifra.Tube.Doors;

/// <summary>
/// The pages a browser lands on (ADR 0008): the host's latest videos, a channel's latest videos and the FAQ. The newest
/// video is the upper left card, the oldest the lower right. No script; the views are Razor components (ADR 0009).
/// </summary>
public static class LandingPage
{
    public const int PageSize = 24;

    // No script; images come from the file door, which redirects to the storage host.
    public const string Policy = "default-src 'none'; img-src 'self' https:; style-src 'unsafe-inline'; frame-ancestors 'none'; base-uri 'none'; form-action 'none'";

    /// <summary>The address of a channel's page, by its NAME (owner decision 2026-11-26, AlcifraTube #28): these pages are
    /// the module's own HTML, never the protocol, so a human-readable, reset-stable segment is free here — and the API
    /// keeps its opaque <c>f-&lt;guid&gt;</c> handle. Sibling-name uniqueness makes the name unambiguous among the
    /// host's channels; a name containing '/' cannot be addressed this way (routing refuses the encoded slash).</summary>
    public static string ChannelAddress(string channelName) => $"/c/{Uri.EscapeDataString(channelName)}";

    /// <summary>The 1-based page, within what exists; anything else that was asked for is the first page.</summary>
    public static int ClampPage(int page, int videoCount) =>
        Math.Clamp(page, 1, Math.Max(1, (videoCount + PageSize - 1) / PageSize));

    /// <summary>A description as far as a card or a link preview shows it.</summary>
    public static string Summary(string description) => description.Length <= 200 ? description : $"{description[..200]}…";

    public static Task<string> Latest(IReadOnlyList<PublishedVideo> videos, IReadOnlyDictionary<Guid, string> channelNames, Account account, int page) =>
        PageRenderer.RenderAsync<LatestView>(new()
        {
            [nameof(LatestView.Videos)] = videos,
            [nameof(LatestView.ChannelNames)] = channelNames,
            [nameof(LatestView.Account)] = account,
            [nameof(LatestView.Page)] = page,
        });

    /// <summary>A playlist's page, under its channel's address, by NAME (ADR 0012; owner decision 2026-11-26): the
    /// playlist name is unique within its channel (sibling rules), so the pair is unambiguous — and stable across a
    /// reset, which the guid was not.</summary>
    public static string PlaylistAddress(string channelName, string playlistName) =>
        $"{ChannelAddress(channelName)}/{Uri.EscapeDataString(playlistName)}";

    /// <param name="addressName">The real channel's name (the address segment); a playlist's page passes its channel's,
    /// because <paramref name="channel"/>'s heading then carries the playlist's name.</param>
    public static Task<string> Channel(Channel channel, IReadOnlyList<PublishedVideo> videos, Account account, int page, IReadOnlyList<ModuleDocument>? playlists = null, string? addressName = null) =>
        PageRenderer.RenderAsync<ChannelView>(new()
        {
            [nameof(ChannelView.Channel)] = channel,
            [nameof(ChannelView.AddressName)] = addressName ?? channel.Name,
            [nameof(ChannelView.Videos)] = videos,
            [nameof(ChannelView.Account)] = account,
            [nameof(ChannelView.Page)] = page,
            [nameof(ChannelView.Playlists)] = playlists ?? [],
        });

    public static Task<string> Faq(Account account) =>
        PageRenderer.RenderAsync<FaqView>(new() { [nameof(FaqView.Account)] = account });
}
