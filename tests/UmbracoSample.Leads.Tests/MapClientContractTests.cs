using System.Diagnostics;
using System.Net;
using System.Text.Json;
using Shouldly;

namespace UmbracoSample.Leads.Tests;

/// <summary>
/// Contract tests for the MAP client against the stub (threat model 0001: repudiation, information disclosure, denial of
/// service and section 5, "never lost, never duplicated"). Timeouts are shortened in <see cref="MapStub"/>: create 1 s,
/// budget 2.5 s; a "slow" stub answers after 1.5 s.
/// </summary>
public sealed class MapClientContractTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_lead_is_created_with_its_key_in_a_header_and_its_consent_in_the_body()
    {
        using var stub = new MapStub();
        stub.OnCreate(r => r.WithStatusCode(HttpStatusCode.Created));
        var submission = MapStub.Submission();

        (await stub.Client.SubmitAsync(submission, Token)).ShouldBe(MapOutcome.Accepted);

        var request = stub.Creates.ShouldHaveSingleItem().RequestMessage!;
        request.Headers!["Idempotency-Key"].ShouldBe([submission.IdempotencyKey]);
        request.Headers["X-Api-Key"].ShouldBe([MapStub.ApiKey]);
        using var body = JsonDocument.Parse(request.Body!);
        var root = body.RootElement;
        root.GetProperty("email").GetString().ShouldBe("ada@example.com");
        root.GetProperty("marketingConsent").GetBoolean().ShouldBeTrue();
        root.GetProperty("consentVersion").GetString().ShouldBe("2026-11");
        root.GetProperty("submittedAt").GetDateTimeOffset().ShouldBe(submission.SubmittedAt);
        root.GetProperty("listId").GetString().ShouldBe("list-1");
    }

    [Fact]
    public async Task Requests_carry_no_trace_context_and_no_key_in_any_url()
    {
        using var listener = new ActivityListener
        {
            ShouldListenTo = _ => true,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
        };
        ActivitySource.AddActivityListener(listener);
        using var source = new ActivitySource("test");
        using var activity = source.StartActivity("submit");
        activity.ShouldNotBeNull();

        using var stub = new MapStub();
        stub.OnCreate(r => r.WithStatusCode(HttpStatusCode.Created).WithDelay(TimeSpan.FromMilliseconds(1500)), r => r.WithStatusCode(HttpStatusCode.Created));
        stub.OnLookup(r => r.WithStatusCode(HttpStatusCode.NotFound));
        var submission = MapStub.Submission();

        (await stub.Client.SubmitAsync(submission, Token)).ShouldBe(MapOutcome.Accepted);

        await MapStub.SettleAsync(TimeSpan.FromMilliseconds(1500));
        stub.Creates.Count.ShouldBe(2);
        stub.Lookups.ShouldNotBeEmpty();
        foreach (var entry in stub.Server.LogEntries)
        {
            entry.RequestMessage!.Headers!.Keys.ShouldNotContain(k => k.Equals("traceparent", StringComparison.OrdinalIgnoreCase) || k.Equals("tracestate", StringComparison.OrdinalIgnoreCase));
            entry.RequestMessage!.AbsoluteUrl.ShouldNotContain(submission.IdempotencyKey);
        }

        // Control: a plain client in the same activity does send trace context, so the check above can fail.
        using var plain = new HttpClient();
        await plain.GetAsync(stub.Server.Url + "/control", Token);
        stub.Server.LogEntries.Last(e => e.RequestMessage!.Path == "/control").RequestMessage!.Headers!.Keys.ShouldContain(k => k.Equals("traceparent", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task A_duplicate_looks_exactly_like_a_new_lead()
    {
        using var stub = new MapStub();
        stub.OnCreate(r => r.WithStatusCode(HttpStatusCode.OK).WithBody("""{"status":"duplicate"}"""));

        (await stub.Client.SubmitAsync(MapStub.Submission(), Token)).ShouldBe(MapOutcome.Accepted);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.UnprocessableEntity)]
    public async Task An_invalid_lead_is_rejected_without_a_retry(HttpStatusCode status)
    {
        using var stub = new MapStub();
        stub.OnCreate(r => r.WithStatusCode(status));

        (await stub.Client.SubmitAsync(MapStub.Submission(), Token)).ShouldBe(MapOutcome.Rejected);
        stub.Creates.Count.ShouldBe(1);
    }

    [Fact]
    public async Task A_server_error_is_retried_once_with_the_same_key()
    {
        using var stub = new MapStub();
        stub.OnCreate(r => r.WithStatusCode(HttpStatusCode.ServiceUnavailable), r => r.WithStatusCode(HttpStatusCode.Created));

        (await stub.Client.SubmitAsync(MapStub.Submission(), Token)).ShouldBe(MapOutcome.Accepted);

        stub.Creates.Count.ShouldBe(2);
        stub.Creates.Select(c => c.RequestMessage!.Headers!["Idempotency-Key"].Single()).Distinct().Count().ShouldBe(1);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.Conflict)]
    public async Task A_refused_call_fails_without_a_retry(HttpStatusCode status)
    {
        using var stub = new MapStub();
        stub.OnCreate(r => r.WithStatusCode(status));

        (await stub.Client.SubmitAsync(MapStub.Submission(), Token)).ShouldBe(MapOutcome.Failed);
        stub.Creates.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Too_many_requests_is_retried_once()
    {
        using var stub = new MapStub();
        stub.OnCreate(r => r.WithStatusCode(HttpStatusCode.TooManyRequests), r => r.WithStatusCode(HttpStatusCode.Created));

        (await stub.Client.SubmitAsync(MapStub.Submission(), Token)).ShouldBe(MapOutcome.Accepted);
        stub.Creates.Count.ShouldBe(2);
    }

    [Fact]
    public async Task An_outage_fails_after_exactly_two_calls_despite_the_standard_resilience_handler()
    {
        using var stub = new MapStub();
        stub.OnCreate(r => r.WithStatusCode(HttpStatusCode.InternalServerError));

        (await stub.Client.SubmitAsync(MapStub.Submission(), Token)).ShouldBe(MapOutcome.Failed);

        stub.Creates.Count.ShouldBe(2);
        stub.Lookups.ShouldBeEmpty();
    }

    [Fact]
    public async Task After_a_timeout_a_lead_the_map_did_take_is_found_by_lookup_and_not_sent_again()
    {
        using var stub = new MapStub();
        stub.OnCreate(r => r.WithStatusCode(HttpStatusCode.Created).WithDelay(TimeSpan.FromMilliseconds(1500)));
        stub.OnLookup(r => r.WithStatusCode(HttpStatusCode.OK));
        var submission = MapStub.Submission();

        (await stub.Client.SubmitAsync(submission, Token)).ShouldBe(MapOutcome.Accepted);

        await MapStub.SettleAsync(TimeSpan.FromMilliseconds(1500));
        stub.Creates.Count.ShouldBe(1);
        using var lookup = JsonDocument.Parse(stub.Lookups.ShouldHaveSingleItem().RequestMessage!.Body!);
        lookup.RootElement.GetProperty("idempotencyKey").GetString().ShouldBe(submission.IdempotencyKey);
    }

    [Fact]
    public async Task After_a_timeout_a_lead_the_map_did_not_take_is_sent_once_more()
    {
        using var stub = new MapStub();
        stub.OnCreate(r => r.WithStatusCode(HttpStatusCode.Created).WithDelay(TimeSpan.FromMilliseconds(1500)), r => r.WithStatusCode(HttpStatusCode.Created));
        stub.OnLookup(r => r.WithStatusCode(HttpStatusCode.NotFound));

        (await stub.Client.SubmitAsync(MapStub.Submission(), Token)).ShouldBe(MapOutcome.Accepted);

        await MapStub.SettleAsync(TimeSpan.FromMilliseconds(1500));
        stub.Creates.Count.ShouldBe(2);
    }

    [Fact]
    public async Task When_the_lookup_cannot_tell_the_outcome_is_unknown_within_the_budget()
    {
        using var stub = new MapStub();
        stub.OnCreate(r => r.WithStatusCode(HttpStatusCode.Created).WithDelay(TimeSpan.FromSeconds(3)));
        stub.OnLookup(r => r.WithStatusCode(HttpStatusCode.OK).WithDelay(TimeSpan.FromSeconds(3)));
        var clock = Stopwatch.StartNew();

        (await stub.Client.SubmitAsync(MapStub.Submission(), Token)).ShouldBe(MapOutcome.Unknown);

        clock.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(3));
        await MapStub.SettleAsync(TimeSpan.FromSeconds(3));
        stub.Creates.Count.ShouldBe(1);
    }

    [Fact]
    public async Task The_budget_caps_the_retry_after_a_timeout()
    {
        // Create timeout 2 s, budget 2.5 s: the first create times out at 2 s, the lookup says "not found" at once, and
        // the retry gets only the 0.5 s that is left. Without the cap, the retry alone would take 2 s more.
        using var stub = new MapStub(settings => settings["Leads:Map:CreateTimeout"] = "00:00:02");
        stub.OnCreate(r => r.WithStatusCode(HttpStatusCode.Created).WithDelay(TimeSpan.FromSeconds(5)));
        stub.OnLookup(r => r.WithStatusCode(HttpStatusCode.NotFound));
        var clock = Stopwatch.StartNew();

        (await stub.Client.SubmitAsync(MapStub.Submission(), Token)).ShouldBe(MapOutcome.Unknown);

        clock.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(3.5));
    }

    [Fact]
    public async Task Running_out_of_budget_before_the_retry_after_a_server_error_is_unknown_not_failed()
    {
        // A 5xx can follow a commit, so giving up before the retry must not tell the visitor the lead was not taken.
        // The pause is at least half of 5 s, longer than the whole 1 s budget.
        using var stub = new MapStub(settings =>
        {
            settings["Leads:Map:MaxRetryJitter"] = "00:00:05";
            settings["Leads:Map:Budget"] = "00:00:01";
        });
        stub.OnCreate(r => r.WithStatusCode(HttpStatusCode.InternalServerError));

        (await stub.Client.SubmitAsync(MapStub.Submission(), Token)).ShouldBe(MapOutcome.Unknown);
        stub.Creates.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Leaving_during_the_pause_before_the_retry_is_unknown_not_failed()
    {
        // The pause is at least 2.5 s; the visitor leaves after 0.5 s, during it.
        using var stub = new MapStub(settings =>
        {
            settings["Leads:Map:MaxRetryJitter"] = "00:00:05";
            settings["Leads:Map:Budget"] = "00:00:10";
        });
        stub.OnCreate(r => r.WithStatusCode(HttpStatusCode.InternalServerError));
        using var leave = CancellationTokenSource.CreateLinkedTokenSource(Token);
        leave.CancelAfter(TimeSpan.FromMilliseconds(500));

        (await stub.Client.SubmitAsync(MapStub.Submission(), leave.Token)).ShouldBe(MapOutcome.Unknown);
        stub.Creates.Count.ShouldBe(1);
        stub.Logs.Lines.ShouldContain(line => line.Contains("LastAttempt=CallerCancelled", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_visitor_who_disconnects_gets_an_unknown_outcome_and_at_most_one_create()
    {
        using var stub = new MapStub();
        stub.OnCreate(r => r.WithStatusCode(HttpStatusCode.Created).WithDelay(TimeSpan.FromMilliseconds(300)));
        using var disconnect = CancellationTokenSource.CreateLinkedTokenSource(Token);
        disconnect.CancelAfter(TimeSpan.FromMilliseconds(100));

        (await stub.Client.SubmitAsync(MapStub.Submission(), disconnect.Token)).ShouldBe(MapOutcome.Unknown);
        stub.Logs.Lines.ShouldContain(line => line.Contains("LastAttempt=CallerCancelled", StringComparison.Ordinal));

        await MapStub.SettleAsync(TimeSpan.FromMilliseconds(300));
        stub.Creates.Count.ShouldBeLessThanOrEqualTo(1);
        stub.Lookups.ShouldBeEmpty();
    }

    [Fact]
    public async Task Over_the_concurrency_cap_a_submission_is_refused_without_calling_the_map()
    {
        using var stub = new MapStub(settings => settings["Leads:Map:MaxConcurrentCalls"] = "1");
        stub.OnCreate(r => r.WithStatusCode(HttpStatusCode.Created).WithDelay(TimeSpan.FromMilliseconds(300)));
        var client = stub.Client;

        var first = client.SubmitAsync(MapStub.Submission(), Token);
        await Task.Delay(50, Token);
        (await client.SubmitAsync(MapStub.Submission(), Token)).ShouldBe(MapOutcome.Overloaded);
        (await first).ShouldBe(MapOutcome.Accepted);

        stub.Creates.Count.ShouldBe(1);
        (await client.SubmitAsync(MapStub.Submission(), Token)).ShouldBe(MapOutcome.Accepted);
    }

    [Fact]
    public async Task Logs_hold_no_field_values_no_key_and_no_echoed_body_on_the_accepted_rejected_failed_and_unknown_paths()
    {
        const string marker = "Zq7Marker";
        using var stub = new MapStub();
        stub.OnCreate(
            r => r.WithStatusCode(HttpStatusCode.UnprocessableEntity).WithBody($$"""{"error":"bad name {{marker}}"}"""),
            r => r.WithStatusCode(HttpStatusCode.InternalServerError).WithBody(marker),
            r => r.WithStatusCode(HttpStatusCode.InternalServerError).WithBody(marker),
            r => r.WithStatusCode(HttpStatusCode.Created).WithDelay(TimeSpan.FromMilliseconds(1500)),
            r => r.WithStatusCode(HttpStatusCode.Created).WithBody(marker));
        stub.OnLookup(r => r.WithStatusCode(HttpStatusCode.InternalServerError).WithBody(marker));
        var submissions = Enumerable.Range(0, 4).Select(_ => MapStub.Submission(name: marker + " Name", message: marker + " message")).ToList();

        (await stub.Client.SubmitAsync(submissions[0], Token)).ShouldBe(MapOutcome.Rejected);
        (await stub.Client.SubmitAsync(submissions[1], Token)).ShouldBe(MapOutcome.Failed);
        (await stub.Client.SubmitAsync(submissions[2], Token)).ShouldBe(MapOutcome.Unknown);
        await MapStub.SettleAsync(TimeSpan.FromMilliseconds(1500));
        (await stub.Client.SubmitAsync(submissions[3], Token)).ShouldBe(MapOutcome.Accepted);

        stub.Logs.Lines.ShouldNotBeEmpty();
        foreach (var line in stub.Logs.Lines)
        {
            line.ShouldNotContain(marker);
            line.ShouldNotContain("ada@example.com");
            line.ShouldNotContain(MapStub.ApiKey);
            foreach (var submission in submissions)
            {
                line.ShouldNotContain(submission.IdempotencyKey);
            }
        }
    }

    [Theory]
    [InlineData("http://map.example.com/")]
    [InlineData("ftp://localhost/")]
    public void Plain_http_is_refused_except_to_a_loopback_stub(string address)
    {
        new MapOptions { BaseAddress = new Uri(address), ApiKey = "k" }.Validate().ShouldNotBeNull();
        new MapOptions { BaseAddress = new Uri("http://localhost:1234/"), ApiKey = "k" }.Validate().ShouldBeNull();
        new MapOptions { BaseAddress = new Uri("https://map.example.com/"), ApiKey = "k" }.Validate().ShouldBeNull();
    }
}
