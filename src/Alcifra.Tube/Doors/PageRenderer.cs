using System.Text.Encodings.Web;
using System.Text.Unicode;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Alcifra.Tube.Doors;

/// <summary>
/// A page's Razor component as the HTML a door answers with (ADR 0009). The render happens inside the module, so the
/// doors stay controllers behind the core's gate and the core needs no view engine for a module. A view injects nothing:
/// a door reads its data and hands it over.
/// </summary>
internal static class PageRenderer
{
    // Every letter stays a letter, rather than the default's numeric reference for anything outside Latin-1's basics.
    private static readonly ServiceProvider Services = new ServiceCollection()
        .AddSingleton(HtmlEncoder.Create(UnicodeRanges.All))
        .BuildServiceProvider();

    public static async Task<string> RenderAsync<TView>(Dictionary<string, object?> parameters) where TView : IComponent
    {
        await using var renderer = new HtmlRenderer(Services, NullLoggerFactory.Instance);
        return await renderer.Dispatcher.InvokeAsync(async () =>
            (await renderer.RenderComponentAsync<TView>(ParameterView.FromDictionary(parameters))).ToHtmlString());
    }
}
