using Microsoft.Extensions.Options;

namespace UmbracoSample.Leads;

/// <summary>
/// Caps the MAP calls in flight on one replica (threat model 0001: a slow MAP must not tie up the site). A submission
/// that finds the cap reached is refused at once rather than queued. Registered as a singleton.
/// </summary>
public sealed class MapBulkhead(IOptions<MapOptions> options) : IDisposable
{
    private readonly SemaphoreSlim _slots = new(options.Value.MaxConcurrentCalls, options.Value.MaxConcurrentCalls);

    public bool TryEnter() => _slots.Wait(0);

    public void Exit() => _slots.Release();

    public void Dispose() => _slots.Dispose();
}
