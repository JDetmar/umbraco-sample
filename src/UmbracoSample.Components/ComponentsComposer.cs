using Microsoft.Extensions.DependencyInjection;
using Umbraco.Cms.Core.Composing;
using Umbraco.Cms.Core.DependencyInjection;

namespace UmbracoSample.Components;

/// <summary>Registers the component services the page views use.</summary>
public sealed class ComponentsComposer : IComposer
{
    public void Compose(IUmbracoBuilder builder) => builder.Services.AddSingleton<PageComponents>();
}
