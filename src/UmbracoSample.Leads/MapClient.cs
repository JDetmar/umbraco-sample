using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace UmbracoSample.Leads;

/// <summary>
/// Sends a lead to the MAP (threat model 0001). The rules it keeps:
/// <list type="bullet">
/// <item>The site owns retries: at most one retry, after a random pause of half to all of <see cref="MapOptions.MaxRetryJitter"/>, always with the same idempotency key (ES-044).</item>
/// <item>A timeout is not a failure (ES-043): after one, the client looks the key up before deciding, and if it cannot
/// tell, the outcome is <see cref="MapOutcome.Unknown"/>.</item>
/// <item>One budget covers the whole submission; the create call gets at most <see cref="MapOptions.CreateTimeout"/>.</item>
/// <item>The idempotency key travels only in a header or a body, never in a URL, and is never logged; neither are
/// request or response bodies, because a MAP error can echo the visitor's input (ES-025).</item>
/// </list>
/// The API it calls is described in docs/map-api.md.
/// </summary>
public sealed partial class MapClient(HttpClient http, MapBulkhead bulkhead, IOptions<MapOptions> options, TimeProvider time, ILogger<MapClient> logger)
{
    internal const string CreatePath = "leads";
    internal const string LookupPath = "leads/lookup";
    internal const string IdempotencyHeader = "Idempotency-Key";
    internal const string ApiKeyHeader = "X-Api-Key";

    private readonly MapOptions _options = options.Value;

    public async Task<MapOutcome> SubmitAsync(MapSubmission submission, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(submission);
        if (!bulkhead.TryEnter())
        {
            LogOutcome(logger, MapOutcome.Overloaded, null);
            return MapOutcome.Overloaded;
        }

        try
        {
            var outcome = await SendAsync(submission, cancellationToken).ConfigureAwait(false);
            return outcome;
        }
        finally
        {
            bulkhead.Exit();
        }
    }

    private async Task<MapOutcome> SendAsync(MapSubmission submission, CancellationToken cancellationToken)
    {
        var started = time.GetTimestamp();
        var retried = false;
        while (true)
        {
            var attempt = await CreateAsync(submission, Remaining(started), cancellationToken).ConfigureAwait(false);
            switch (attempt)
            {
                case Attempt.Accepted:
                    return Done(MapOutcome.Accepted, attempt);
                case Attempt.Rejected:
                    return Done(MapOutcome.Rejected, attempt);
                case Attempt.Refused:
                    // The MAP refused the call itself (credential or conflict): no retry can help, and nothing was stored.
                    return Done(MapOutcome.Failed, attempt);
                case Attempt.CallerCancelled:
                    // The request may have reached the MAP; the next submission's idempotency key reconciles it.
                    return Done(MapOutcome.Unknown, attempt);
                case Attempt.TimedOut:
                    var found = await LookupAsync(submission, Remaining(started), cancellationToken).ConfigureAwait(false);
                    if (found is Lookup.Found)
                    {
                        return Done(MapOutcome.Accepted, attempt);
                    }

                    if (found is not Lookup.NotFound || retried)
                    {
                        return Done(MapOutcome.Unknown, attempt);
                    }

                    break;
                default:
                    if (retried)
                    {
                        return Done(MapOutcome.Failed, attempt);
                    }

                    break;
            }

            // Whatever went wrong may have been stored (a 5xx can follow a commit), so leaving before the retry because the
            // budget ran out or the visitor left is "unknown", never "failed" (threat model 0001: an exhausted budget is
            // never "failed").
            retried = true;
            var pause = _options.MaxRetryJitter * (0.5 + (Random.Shared.NextDouble() / 2)); // between half and all of it
            if (Remaining(started) <= pause)
            {
                return Done(MapOutcome.Unknown, attempt);
            }

            try
            {
                await Task.Delay(pause, time, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return Done(MapOutcome.Unknown, Attempt.CallerCancelled);
            }
        }
    }

    private async Task<Attempt> CreateAsync(MapSubmission submission, TimeSpan remaining, CancellationToken cancellationToken)
    {
        if (remaining <= TimeSpan.Zero)
        {
            return Attempt.TimedOut;
        }

        using var limit = new CancellationTokenSource(Min(_options.CreateTimeout, remaining), time);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, limit.Token);
        using var request = new HttpRequestMessage(HttpMethod.Post, CreatePath) { Content = JsonContent.Create(CreateBody.From(submission), MapJsonContext.Default.CreateBody) };
        request.Headers.Add(IdempotencyHeader, submission.IdempotencyKey);
        request.Headers.Add(ApiKeyHeader, _options.ApiKey);
        try
        {
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
            return response.StatusCode switch
            {
                HttpStatusCode.OK or HttpStatusCode.Created => Attempt.Accepted,
                HttpStatusCode.BadRequest or HttpStatusCode.UnprocessableEntity => Attempt.Rejected,
                HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden or HttpStatusCode.Conflict => Attempt.Refused,
                _ => Attempt.Retryable,
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return Attempt.CallerCancelled;
        }
        catch (OperationCanceledException)
        {
            return Attempt.TimedOut;
        }
        catch (HttpRequestException)
        {
            // Connection refused, reset or a TLS failure. The request may have been sent, but the key makes a retry safe.
            return Attempt.Retryable;
        }
    }

    private async Task<Lookup> LookupAsync(MapSubmission submission, TimeSpan remaining, CancellationToken cancellationToken)
    {
        if (remaining <= TimeSpan.Zero)
        {
            return Lookup.Unavailable;
        }

        using var limit = new CancellationTokenSource(Min(_options.CreateTimeout, remaining), time);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, limit.Token);
        using var request = new HttpRequestMessage(HttpMethod.Post, LookupPath) { Content = JsonContent.Create(new LookupBody(submission.IdempotencyKey), MapJsonContext.Default.LookupBody) };
        request.Headers.Add(ApiKeyHeader, _options.ApiKey);
        try
        {
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
            return response.StatusCode switch
            {
                HttpStatusCode.OK => Lookup.Found,
                HttpStatusCode.NotFound => Lookup.NotFound,
                _ => Lookup.Unavailable,
            };
        }
        catch (Exception exception) when (exception is OperationCanceledException or HttpRequestException)
        {
            return Lookup.Unavailable;
        }
    }

    private TimeSpan Remaining(long started) => _options.Budget - time.GetElapsedTime(started);

    private static TimeSpan Min(TimeSpan a, TimeSpan b) => a < b ? a : b;

    private MapOutcome Done(MapOutcome outcome, Attempt last)
    {
        LogOutcome(logger, outcome, last);
        return outcome;
    }

    // Only the outcome and the kind of the last attempt: no key, no field values, no bodies.
    [LoggerMessage(EventId = 4100, Level = LogLevel.Information, Message = "Lead submission to the MAP: {Outcome} (last attempt: {LastAttempt})")]
    private static partial void LogOutcome(ILogger logger, MapOutcome outcome, Attempt? lastAttempt);

    internal enum Attempt
    {
        Accepted,
        Rejected,
        Refused,
        Retryable,
        TimedOut,
        CallerCancelled,
    }

    private enum Lookup
    {
        Found,
        NotFound,
        Unavailable,
    }
}

internal sealed record CreateBody(
    string Name,
    string Email,
    string? Company,
    string? Message,
    bool MarketingConsent,
    string ConsentVersion,
    DateTimeOffset SubmittedAt,
    string ListId)
{
    public static CreateBody From(MapSubmission submission) => new(
        submission.Lead.Name,
        submission.Lead.Email.Value,
        submission.Lead.Company,
        submission.Lead.Message,
        submission.Lead.MarketingConsent,
        submission.Lead.ConsentVersion,
        submission.SubmittedAt,
        submission.ListId);
}

internal sealed record LookupBody(string IdempotencyKey);

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(CreateBody))]
[JsonSerializable(typeof(LookupBody))]
internal sealed partial class MapJsonContext : JsonSerializerContext;
