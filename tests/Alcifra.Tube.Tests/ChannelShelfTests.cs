using Alcifra.Tube.Api;
using Alcifra.Tube.Catalogue;
using Alcifra.Tube.Module;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Alcifra.ModuleAbi;

namespace Alcifra.Tube.Tests;

// The Tube tab's list (ADR 0007): the person's own rights decide what shows, drafts only for who may edit them, a lock on a
// channel they can't write to, and only a reference can leave a channel.
public class ChannelShelfTests
{
    private static readonly Guid Tenant = Guid.Parse("22222222-3333-4444-8555-666666666666");
    private static readonly Guid Open = Guid.Parse("10000000-0000-4000-8000-000000000001");
    private static readonly Guid Locked = Guid.Parse("10000000-0000-4000-8000-000000000002");
    private static readonly Guid Hidden = Guid.Parse("10000000-0000-4000-8000-000000000003");
    private static readonly Guid Workshop = Guid.Parse("10000000-0000-4000-8000-000000000004");
    private static readonly Guid Live = Guid.Parse("20000000-0000-4000-8000-000000000001");
    private static readonly Guid EditableDraft = Guid.Parse("20000000-0000-4000-8000-000000000002");
    private static readonly Guid ForeignDraft = Guid.Parse("20000000-0000-4000-8000-000000000003");
    private static readonly Guid Prepared = Guid.Parse("20000000-0000-4000-8000-000000000004");
    private static readonly Guid Secret = Guid.Parse("20000000-0000-4000-8000-000000000005");

    private static readonly ModuleDocumentRightsAnswer Nothing = new(false, false, false, false, false, false, false, false, false);
    private static readonly ModuleDocumentRightsAnswer Reader = Nothing with { CanSee = true, CanReadContent = true };
    private static readonly ModuleDocumentRightsAnswer Editor = Reader with { CanEditIndexData = true };
    private static readonly ModuleDocumentRightsAnswer Writer = Reader with { CanCreateSubItems = true };

    private sealed class StubRights(Dictionary<Guid, ModuleDocumentRightsAnswer> answers) : IModuleDocumentRights
    {
        public Task<ModuleDocumentRightsAnswer> GetAsync(Guid documentId, CancellationToken cancellationToken = default) =>
            Task.FromResult(answers.GetValueOrDefault(documentId, Nothing));
    }

    private static ChannelShelf Shelf(bool encrypts = false, string? host = "video.example.test")
    {
        IReadOnlyList<ModuleDocument> documents =
        [
            new(Open, null, "Open", TubeChannel.MaskId, new Dictionary<string, string>()),
            new(Locked, null, "Locked", TubeChannel.MaskId, new Dictionary<string, string>()),
            new(Hidden, null, "Hidden", TubeChannel.MaskId, new Dictionary<string, string>()),
            Clip(Live, Open, "Live.mp4", "yes"),
            Clip(EditableDraft, Open, "Editable draft.mp4", "no"),
            Clip(ForeignDraft, Open, "Foreign draft.mp4", "no"),
            Clip(Prepared, Workshop, "Prepared.mp4", "yes"),
            Clip(Secret, Hidden, "Secret.mp4", "yes"),
        ];
        var settings = new Dictionary<string, string>();
        if (host is not null)
        {
            settings[TubeSettings.HostKey] = host;
        }

        var archive = new StubArchive(documents, settings, id => new Uri($"https://storage.example.test/{id:D}"));
        var at = DateTimeOffset.Parse("2026-11-01T09:00:00Z");
        archive.References.Add((Open, Prepared, at));
        archive.References.Add((Locked, Prepared, at));
        var caller = new StubCaller(Tenant, encrypts);
        var catalogue = new VideoCatalogue(archive, caller, new EncryptionWarnings(), NullLogger<VideoCatalogue>.Instance);
        var rights = new StubRights(new()
        {
            [Open] = Writer,
            [Locked] = Reader,
            [Live] = Reader,
            [EditableDraft] = Editor,
            [ForeignDraft] = Reader,
            [Prepared] = Reader,
            [Secret] = Reader,
        });
        return new ChannelShelf(archive, rights, caller, catalogue);
    }

    private static ModuleDocument Clip(Guid id, Guid parent, string name, string published) =>
        new(id, parent, name, Video.MaskId, new Dictionary<string, string> { [Video.Published] = published });

    private static ChannelShelfVideo In(ChannelShelfResource shelf, Guid channel, Guid video) =>
        shelf.Items.Single(c => c.Id == channel).Videos.Single(v => v.Id == video);

    [Fact]
    public async Task Only_the_channels_the_person_can_see_are_listed()
    {
        var shelf = await Shelf().ReadAsync(CancellationToken.None);
        Assert.Equal([Locked, Open], shelf.Items.Select(c => c.Id));
    }

    [Fact]
    public async Task A_draft_shows_only_to_who_may_edit_it()
    {
        var videos = (await Shelf().ReadAsync(CancellationToken.None)).Items.Single(c => c.Id == Open).Videos;
        Assert.Contains(videos, v => v.Id == EditableDraft && !v.Published && v.CanEdit);
        Assert.DoesNotContain(videos, v => v.Id == ForeignDraft);
    }

    [Fact]
    public async Task A_channel_the_person_cannot_write_to_is_locked_and_nothing_leaves_it()
    {
        var shelf = await Shelf().ReadAsync(CancellationToken.None);
        Assert.True(shelf.Items.Single(c => c.Id == Open).CanWrite);
        Assert.False(shelf.Items.Single(c => c.Id == Locked).CanWrite);
        Assert.False(In(shelf, Locked, Prepared).CanRemoveFromChannel);
    }

    [Fact]
    public async Task A_video_in_two_channels_is_listed_in_both_and_only_a_reference_can_leave()
    {
        var shelf = await Shelf().ReadAsync(CancellationToken.None);
        Assert.True(In(shelf, Open, Prepared).IsReference);
        Assert.True(In(shelf, Open, Prepared).CanRemoveFromChannel);
        Assert.True(In(shelf, Locked, Prepared).IsReference);
        Assert.False(In(shelf, Open, Live).IsReference);
        Assert.False(In(shelf, Open, Live).CanRemoveFromChannel);
    }

    [Fact]
    public async Task The_watch_page_is_named_only_for_a_published_video_on_a_named_host()
    {
        var shelf = await Shelf().ReadAsync(CancellationToken.None);
        Assert.Equal($"https://video.example.test/videos/watch/{Live:D}", In(shelf, Open, Live).WatchUrl);
        Assert.Null(In(shelf, Open, EditableDraft).WatchUrl);

        var hostless = await Shelf(host: null).ReadAsync(CancellationToken.None);
        Assert.Null(In(hostless, Open, Live).WatchUrl);
    }

    [Fact]
    public async Task A_tenant_that_encrypts_at_rest_lists_nothing_and_says_why()
    {
        var shelf = await Shelf(encrypts: true).ReadAsync(CancellationToken.None);
        Assert.True(shelf.EncryptsAtRest);
        Assert.Empty(shelf.Items);
    }

    [Fact]
    public async Task The_controller_answers_with_its_own_link()
    {
        var answer = await new ChannelsController(Shelf()).Get(CancellationToken.None);
        Assert.IsType<ActionResult<ChannelShelfResource>>(answer);
        Assert.Contains(answer.Value!.Links, l => l.Rel == "self" && l.Href == $"/{ChannelsController.Base}");
    }

    [Fact]
    public async Task A_draft_plays_for_who_may_edit_it_and_nothing_plays_outside_the_shelf()
    {
        var shelf = Shelf();
        Assert.Equal("video/mp4", (await shelf.PlayAsync(EditableDraft, CancellationToken.None))!.ContentType);
        Assert.Null(await shelf.PlayAsync(ForeignDraft, CancellationToken.None));
        Assert.Null(await shelf.PlayAsync(Secret, CancellationToken.None));
        Assert.Null(await shelf.PlayAsync(Guid.NewGuid(), CancellationToken.None));
    }

    [Fact]
    public async Task The_rows_advertise_play_only_to_who_may_read_the_file()
    {
        var video = In(await Shelf().ReadAsync(CancellationToken.None), Open, Live);
        Assert.Contains(video.Links, l => l.Rel == "play" && l.Href == $"/{ChannelsController.Base}/videos/{Live:D}/play");
        Assert.DoesNotContain(video.Links, l => l.Rel == "thumbnail");
        Assert.Contains(video.Links, l => l.Rel == "document" && l.Href == $"/api/documents/{Live:D}");
    }

    [Fact]
    public void The_module_advertises_the_tab_by_a_rel() =>
        Assert.Contains(new TubeModule().RootLinks, l => l.Rel == "tube:channels" && l.Method == "GET");
}
