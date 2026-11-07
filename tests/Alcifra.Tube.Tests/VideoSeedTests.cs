using Alcifra.Tube.Module;

namespace Alcifra.Tube.Tests;

// A video's Thumbnail names only a Video Thumbnail (ABI 2.1, core ADR 1068, core #2214): the core refuses any other target,
// and the clients' picker offers only thumbnails, with "From a file" filing a new one beside the video.
public class VideoSeedTests
{
    [Fact]
    public void The_thumbnail_field_accepts_only_a_video_thumbnail()
    {
        var thumbnail = Video.Mask.Fields.Single(f => f.Name == Video.Thumbnail);

        Assert.Equal("DocumentReference", thumbnail.DataType);
        Assert.Equal([VideoThumbnail.MaskId], thumbnail.AcceptedMasks);
    }
}
