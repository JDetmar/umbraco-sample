using System.Collections.Concurrent;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using UmbracoSample.Domain.Leads;
using WireMock.Logging;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace UmbracoSample.Leads.Tests;

/// <summary>
/// A WireMock.Net stub of the MAP API in docs/map-api.md, and a <see cref="MapClient"/> built the way a host builds it:
/// with ServiceDefaults' standard resilience handler registered for every client, so the tests prove the MAP client
/// opts out of it.
/// </summary>
internal sealed class MapStub : IDisposable
{
    public const string ApiKey = "test-key-not-a-secret";

    private readonly IHost _host;

    public MapStub(Action<Dictionary<string, string?>>? configure = null)
    {
        Server = WireMockServer.Start();
        var settings = new Dictionary<string, string?>
        {
            ["Leads:Map:BaseAddress"] = Server.Url + "/api",
            ["Leads:Map:ApiKey"] = ApiKey,
            ["Leads:Map:CreateTimeout"] = "00:00:01",
            ["Leads:Map:Budget"] = "00:00:02.500",
            ["Leads:Map:MaxRetryJitter"] = "00:00:00.020",
        };
        configure?.Invoke(settings);
        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());
        builder.Configuration.AddInMemoryCollection(settings);
        builder.AddServiceDefaults();
        builder.Logging.AddProvider(Logs).SetMinimumLevel(LogLevel.Trace);
        builder.Services.AddMapClient(builder.Configuration);
        _host = builder.Build();

        // An unmapped lookup would get WireMock's 404, which reads as "not found" and triggers a create. A test that does
        // not script the lookup gets "cannot tell" instead, so it never retries by accident.
        Server.Given(Request.Create().WithPath("/api/leads/lookup").UsingPost()).AtPriority(1000).RespondWith(Response.Create().WithStatusCode(599));
    }

    public WireMockServer Server { get; }

    public CapturingLoggerProvider Logs { get; } = new();

    public MapClient Client => _host.Services.GetRequiredService<MapClient>();

    public IReadOnlyList<ILogEntry> Creates => [.. Server.LogEntries.Where(e => e.RequestMessage!.Path == "/api/leads")];

    public IReadOnlyList<ILogEntry> Lookups => [.. Server.LogEntries.Where(e => e.RequestMessage!.Path == "/api/leads/lookup")];

    /// <summary>
    /// WireMock logs a request when its response is done, so a delayed request shows up only after its delay, even when
    /// the client gave up earlier. Wait that long before counting.
    /// </summary>
    public static Task SettleAsync(TimeSpan delay) => Task.Delay(delay + TimeSpan.FromMilliseconds(300), TestContext.Current.CancellationToken);

    public void OnCreate(params Func<IResponseBuilder, IResponseBuilder>[] responses) => Sequence("/api/leads", responses);

    public void OnLookup(params Func<IResponseBuilder, IResponseBuilder>[] responses) => Sequence("/api/leads/lookup", responses);

    public static MapSubmission Submission(string name = "Ada Lovelace", string message = "Hello") => new(
        LeadParser.Parse(
            new LeadInput(name, "ada@example.com", "Analytical Engines", message, true, "2026-11"),
            new LeadRules(LeadLimits.Default, new HashSet<string>(["2026-11"], StringComparer.Ordinal))).Lead!,
        "list-1",
        "key-" + Guid.NewGuid().ToString("N"),
        new DateTimeOffset(2026, 11, 26, 9, 0, 0, TimeSpan.Zero));

    public void Dispose()
    {
        _host.Dispose();
        Server.Stop();
        Server.Dispose();
    }

    // Each response in turn; the last one repeats.
    private void Sequence(string path, Func<IResponseBuilder, IResponseBuilder>[] responses)
    {
        var scenario = path + Guid.NewGuid();
        for (var i = 0; i < responses.Length; i++)
        {
            var mapping = Server.Given(Request.Create().WithPath(path).UsingPost()).InScenario(scenario);
            if (i > 0)
            {
                mapping = mapping.WhenStateIs($"call-{i}");
            }

            if (i < responses.Length - 1)
            {
                mapping = mapping.WillSetStateTo($"call-{i + 1}");
            }

            mapping.RespondWith(responses[i](Response.Create()));
        }
    }
}

internal sealed class CapturingLoggerProvider : ILoggerProvider
{
    public ConcurrentQueue<string> Lines { get; } = new();

    public ILogger CreateLogger(string categoryName) => new Logger(this, categoryName);

    public void Dispose()
    {
    }

    private sealed class Logger(CapturingLoggerProvider owner, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            var values = state is IEnumerable<KeyValuePair<string, object?>> pairs ? string.Join(";", pairs.Select(p => $"{p.Key}={p.Value}")) : string.Empty;
            owner.Lines.Enqueue($"{category} {formatter(state, exception)} {values} {exception}");
        }
    }
}
