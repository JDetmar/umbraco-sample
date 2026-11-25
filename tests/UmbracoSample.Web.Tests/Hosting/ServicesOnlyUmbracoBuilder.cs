using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Umbraco.Cms.Core.Cache;
using Umbraco.Cms.Core.Composing;
using Umbraco.Cms.Core.Logging;

namespace UmbracoSample.Web.Tests.Hosting;

/// <summary>An Umbraco builder with only a service collection, for composers that only register services.</summary>
internal sealed class ServicesOnlyUmbracoBuilder : IUmbracoBuilder
{
    public IServiceCollection Services { get; } = new ServiceCollection();

    public IConfiguration Config => throw new NotSupportedException();

    public TypeLoader TypeLoader => throw new NotSupportedException();

    public ILoggerFactory BuilderLoggerFactory => throw new NotSupportedException();

    public IProfiler Profiler => throw new NotSupportedException();

    public AppCaches AppCaches => throw new NotSupportedException();

    public TBuilder WithCollectionBuilder<TBuilder>()
        where TBuilder : ICollectionBuilder => throw new NotSupportedException();

    public void Build() => throw new NotSupportedException();
}
