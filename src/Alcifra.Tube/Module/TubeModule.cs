using Microsoft.Extensions.DependencyInjection;
using Alcifra.ModuleAbi;

namespace Alcifra.Tube.Module;

/// <summary>The video module (ADR 0001): free, one mask, three settings, served to anyone on the tenant's video host.</summary>
public sealed class TubeModule : IIndustryModule
{
    public const string Id = "tube";
    public const string RootPrefix = "peertube";

    public string ModuleId => Id;
    public string DisplayName => "Tube";
    public int AbiMajorVersion => ModuleAbiVersion.Major;
    public int AbiMinorVersion => ModuleAbiVersion.Minor;

    /// <summary>Free and open source (ABI 1.23): no licence file, so no key to verify one with.</summary>
    public bool RequiresLicence => false;
    public string LicenseVerifyKeyPem => string.Empty;

    public IReadOnlyList<ModuleMaskSeed> Masks { get; } = [TubeChannel.Mask, Video.Mask, VideoThumbnail.Mask];

    public IReadOnlyList<ModuleSetting> Settings { get; } =
    [
        new(TubeSettings.HostKey, "Video host",
            Description: "The host name PeerTube clients use for this tenant's videos, e.g. video.example.com, proxied to the core's peertube prefix. Empty serves nothing."),
        new(TubeSettings.NameKey, "Instance name", Description: "What clients show as the instance's name; the tenant's name when empty."),
        new(TubeSettings.DescriptionKey, "Instance description", Description: "A sentence clients show about the instance."),
    ];

    /// <summary>The PeerTube API and the file door, for anyone on the host the tenant named (ABI 1.28, core ADR 1038).</summary>
    public IReadOnlyList<AnonymousRootPrefix> AnonymousRootPrefixes { get; } = [new AnonymousRootPrefix(RootPrefix, TubeSettings.HostKey)];

    /// <summary>The Tube tab's list for a signed-in person (ADR 0007); a client offers the tab where this rel is advertised.</summary>
    public IReadOnlyList<ModuleRootLink> RootLinks { get; } = [new("tube:channels", $"/{Api.ChannelsController.Base}", "GET")];

    public ModuleManual? Manual => new("Alcifra Tube Module Manual.pdf",
        () => typeof(TubeModule).Assembly.GetManifestResourceStream("Alcifra.Tube.Manual.pdf")
            ?? throw new InvalidOperationException("The manual is not embedded in this build."));

    public void ConfigureServices(IServiceCollection services)
    {
        services.AddScoped<Catalogue.VideoCatalogue>();
        services.AddScoped<Catalogue.VideoDirectory>();
        services.AddScoped<Catalogue.ChannelShelf>();
        services.AddSingleton<Catalogue.EncryptionWarnings>();
    }
}

/// <summary>The settings the core renders on the module's form, per tenant.</summary>
public static class TubeSettings
{
    public const string HostKey = "tube.host";
    public const string NameKey = "tube.name";
    public const string DescriptionKey = "tube.description";
}
