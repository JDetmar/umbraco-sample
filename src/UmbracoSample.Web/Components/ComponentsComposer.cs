using Umbraco.Cms.Core.Composing;

namespace UmbracoSample.Web.Components;

/// <summary>Registers the component services the page views use.</summary>
public sealed class ComponentsComposer : IComposer
{
    public void Compose(IUmbracoBuilder builder) => builder.Services.AddSingleton<PageComponents>();
}
