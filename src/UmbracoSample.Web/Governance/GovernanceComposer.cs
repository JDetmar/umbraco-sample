using Microsoft.Extensions.DependencyInjection.Extensions;
using Umbraco.Cms.Core.Composing;
using Umbraco.Cms.Core.DependencyInjection;

namespace UmbracoSample.Web.Governance;

/// <summary>Gives the content governance health check a clock it can be tested with (ADR 0018).</summary>
public sealed class GovernanceComposer : IComposer
{
    public void Compose(IUmbracoBuilder builder) => builder.Services.TryAddSingleton(TimeProvider.System);
}
