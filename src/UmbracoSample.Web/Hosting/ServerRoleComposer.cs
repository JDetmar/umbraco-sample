using Umbraco.Cms.Core.Composing;
using Umbraco.Cms.Core.Sync;
using Umbraco.Cms.Infrastructure.DependencyInjection;

namespace UmbracoSample.Web.Hosting;

/// <summary>
/// The site always runs as one Umbraco server (SiteSettings refuses more than one replica, DEV-015). Without this,
/// Umbraco elects roles through the shared database, so a new Container Apps revision that starts while the previous
/// one is still running becomes a Subscriber, and uSync skips its startup import. Declaring the role makes every
/// instance the single server; the container app keeps one active revision so no older one runs beside it (ADR 0015).
/// </summary>
public sealed class ServerRoleComposer : IComposer
{
    public void Compose(IUmbracoBuilder builder) => builder.SetServerRegistrar<SingleServerRoleAccessor>();
}
