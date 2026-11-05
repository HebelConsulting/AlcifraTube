using Alcifra.Tube.Catalogue;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Alcifra.ModuleAbi;

namespace Alcifra.Tube.Api;

/// <summary>The Tube tab's list (ADR 0007), for a signed-in person; reached from the root's <c>tube:channels</c> rel.</summary>
[ApiController]
[Authorize]
[Route(Base)]
public sealed class ChannelsController(ChannelShelf shelf) : ControllerBase
{
    public const string Base = "api/modules/tube/channels";

    [HttpGet]
    public async Task<ActionResult<ChannelShelfResource>> Get(CancellationToken cancellationToken)
    {
        var answer = await shelf.ReadAsync(cancellationToken);
        answer.Links = [new Link("self", $"/{Base}", "GET")];
        return answer;
    }

    // Standing convention: every GET has its own HEAD.
    [HttpHead]
    public async Task<IActionResult> Head(CancellationToken cancellationToken)
    {
        await shelf.ReadAsync(cancellationToken);
        return Ok();
    }

    [HttpGet("videos/{videoId:guid}/play")]
    public async Task<ActionResult<ContentLinkResource>> Play(Guid videoId, CancellationToken cancellationToken) =>
        Fresh(await shelf.PlayAsync(videoId, cancellationToken));

    [HttpHead("videos/{videoId:guid}/play")]
    public Task<ActionResult<ContentLinkResource>> PlayHead(Guid videoId, CancellationToken cancellationToken) => Play(videoId, cancellationToken);

    [HttpGet("videos/{videoId:guid}/thumbnail")]
    public async Task<ActionResult<ContentLinkResource>> Thumbnail(Guid videoId, CancellationToken cancellationToken) =>
        Fresh(await shelf.ThumbnailAsync(videoId, cancellationToken));

    [HttpHead("videos/{videoId:guid}/thumbnail")]
    public Task<ActionResult<ContentLinkResource>> ThumbnailHead(Guid videoId, CancellationToken cancellationToken) => Thumbnail(videoId, cancellationToken);

    private ActionResult<ContentLinkResource> Fresh(ContentLinkResource? link)
    {
        if (link is null)
        {
            return NotFound();
        }

        Response.Headers.CacheControl = "no-store";
        return link;
    }
}
