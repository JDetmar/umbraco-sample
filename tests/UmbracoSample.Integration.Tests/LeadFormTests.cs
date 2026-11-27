using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace UmbracoSample.Integration.Tests;

/// <summary>
/// The lead form through the real site and the local MAP stub (threat model 0001, section 8: the evidence step 4
/// owes). Each test posts as its own client IP, so the per-client rate limit of 3 per 10 minutes never couples tests.
/// The tests share the stub's lead store, so they run one at a time in this class.
/// </summary>
[Trait("Category", "Integration")]
public sealed partial class LeadFormTests(SiteFixture site)
{
    private const string FormPage = "/fixture-form/";

    // The site refuses a post sooner than 3 s after the render (bot screen); wait a little longer.
    private static readonly TimeSpan HumanPause = TimeSpan.FromSeconds(3.2);

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_form_page_is_never_stored_and_every_render_gets_its_own_token()
    {
        using var visitor = Visitor();

        var first = await visitor.OpenAsync();
        var second = await visitor.OpenAsync();

        first.CacheControl.ShouldContain("no-store");
        first.CacheControl.ShouldContain("private", Case.Sensitive, "the site's own value, not only the one antiforgery sets");
        first.RenderToken.ShouldNotBe(second.RenderToken, "each render carries its own idempotency key");
    }

    [Fact]
    public async Task A_post_without_the_antiforgery_token_gets_400()
    {
        using var visitor = Visitor();
        var form = await visitor.OpenAsync();

        using var response = await visitor.PostAsync(form, Lead(), antiforgery: false);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task A_post_over_16_KB_gets_413_and_is_not_read()
    {
        using var visitor = Visitor();
        var form = await visitor.OpenAsync();

        using var response = await visitor.PostAsync(form, Lead() with { Message = new string('x', 17 * 1024) });

        response.StatusCode.ShouldBe(HttpStatusCode.RequestEntityTooLarge);
    }

    [Fact]
    public async Task A_post_over_16_KB_to_umbracos_direct_surface_route_gets_413_too()
    {
        using var visitor = Visitor();
        var form = await visitor.OpenAsync();

        using var response = await visitor.PostAsync(form, Lead() with { Message = new string('x', 17 * 1024) }, path: "/umbraco/surface/LeadForm/Submit");

        response.StatusCode.ShouldBe(HttpStatusCode.RequestEntityTooLarge);
    }

    [Fact]
    public async Task A_chunked_post_over_16_KB_is_refused_and_sends_no_lead()
    {
        await ClearLeadsAsync();
        using var visitor = Visitor();
        var form = await visitor.OpenAsync();
        await Task.Delay(HumanPause, Cancel);

        // A valid lead padded with a field the site ignores: without the cap this post would be thanked and sent.
        using var response = await visitor.PostAsync(form, Lead(), chunked: true, extra: [("Padding", new string('x', 17 * 1024))]);

        // With no Content-Length the server cannot refuse the body up front; it stops reading at 16 KB instead. Who reads
        // first decides the status: a 413, a 400 from antiforgery, or Umbraco giving up on the form and rendering the
        // page. Whatever the status, the post is never handled.
        ((int)response.StatusCode).ShouldBeOneOf(200, 400, 413);
        (await response.Content.ReadAsStringAsync(Cancel)).ShouldNotContain("Thanks, we will be in touch.");
        (await LeadsAsync()).ShouldBeEmpty();
    }

    [Fact]
    public async Task A_token_rendered_on_one_page_does_not_submit_on_another()
    {
        await ClearLeadsAsync();
        using var visitor = Visitor();
        var form = await visitor.OpenAsync();
        await Task.Delay(HumanPause, Cancel);

        using var response = await visitor.PostAsync(form, Lead(), path: "/fixture-minimal/");

        (await response.Content.ReadAsStringAsync(Cancel)).ShouldNotContain("Thanks, we will be in touch.");
        (await LeadsAsync()).ShouldBeEmpty();
    }

    [Fact]
    public async Task The_fourth_post_from_one_client_in_the_window_gets_429()
    {
        using var visitor = Visitor();
        var form = await visitor.OpenAsync();

        // Every attempt counts, even ones the site refuses as too fast.
        for (var i = 0; i < 3; i++)
        {
            using var allowed = await visitor.PostAsync(form, Lead());
            allowed.StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        using var limited = await visitor.PostAsync(form, Lead());
        limited.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        limited.Headers.CacheControl?.NoStore.ShouldBeTrue();

        using var other = Visitor();
        using var otherClient = await other.PostAsync(await other.OpenAsync(), Lead());
        otherClient.StatusCode.ShouldBe(HttpStatusCode.OK, "another client has its own window");
    }

    [Fact]
    public async Task A_lead_goes_to_the_blocks_list_and_a_posted_list_id_is_ignored()
    {
        await ClearLeadsAsync();
        using var visitor = Visitor();
        var form = await visitor.OpenAsync();
        await Task.Delay(HumanPause, Cancel);

        using var response = await visitor.PostAsync(form, Lead(), ("listId", "attacker-chosen-list"));
        var page = await response.Content.ReadAsStringAsync(Cancel);

        page.ShouldContain("Thanks, we will be in touch.");
        page.ShouldMatch(@"Thanks, we will be in touch\.</p>\s*</section>", "the thank-you closes the form's region");
        var lead = (await LeadsAsync()).ShouldHaveSingleItem();
        lead.ListId.ShouldBe("synthetic-sales-list");
        lead.ConsentVersion.ShouldBe("2026-11");
    }

    [Fact]
    public async Task An_invalid_post_re_renders_with_the_input_errors_and_the_same_token()
    {
        using var visitor = Visitor();
        var form = await visitor.OpenAsync();
        await Task.Delay(HumanPause, Cancel);

        using var response = await visitor.PostAsync(form, Lead() with { Email = "not-an-email", Company = "Kept Company" });
        var again = RenderedForm.Parse(await response.Content.ReadAsStringAsync(Cancel), response);

        again.Html.ShouldContain("Please check the form");
        again.Html.ShouldContain("aria-invalid=\"true\"");
        again.Html.ShouldContain("value=\"Kept Company\"");
        again.RenderToken.ShouldBe(form.RenderToken);
    }

    [Fact]
    public async Task An_outage_never_says_thank_you_and_the_resubmission_keeps_its_key()
    {
        await ClearLeadsAsync();
        using var visitor = Visitor();
        var form = await visitor.OpenAsync();
        await Task.Delay(HumanPause, Cancel);

        RenderedForm failed;
        await SetOutageAsync(true);
        try
        {
            using var response = await visitor.PostAsync(form, Lead());
            failed = RenderedForm.Parse(await response.Content.ReadAsStringAsync(Cancel), response);
        }
        finally
        {
            await SetOutageAsync(false);
        }

        failed.Html.ShouldNotContain("Thanks, we will be in touch.");
        failed.Html.ShouldContain("Please try again.");
        failed.RenderToken.ShouldBe(form.RenderToken, "the re-render keeps the posted token, so the retry keeps its key");

        using var retry = await visitor.PostAsync(failed, Lead());
        (await retry.Content.ReadAsStringAsync(Cancel)).ShouldContain("Thanks, we will be in touch.");
        (await LeadsAsync()).ShouldHaveSingleItem();
    }

    [Fact]
    public async Task A_double_submission_creates_one_lead()
    {
        await ClearLeadsAsync();
        using var visitor = Visitor();
        var form = await visitor.OpenAsync();
        await Task.Delay(HumanPause, Cancel);

        var posts = await Task.WhenAll(visitor.PostAsync(form, Lead()), visitor.PostAsync(form, Lead()));
        foreach (var post in posts)
        {
            using (post)
            {
                (await post.Content.ReadAsStringAsync(Cancel)).ShouldContain("Thanks, we will be in touch.");
            }
        }

        (await LeadsAsync()).ShouldHaveSingleItem();
    }

    [Fact]
    public async Task Logs_and_spans_hold_none_of_the_visitors_values_the_key_or_the_client_ip()
    {
        await ClearLeadsAsync();
        await using var siteLogs = new SiteLogs(site);
        var marker = Guid.NewGuid().ToString("N");
        var lead = new LeadValues($"Name{marker}", $"mail{marker}@example.com", $"Company{marker}", $"Message{marker}");
        using var visitor = Visitor();
        var form = await visitor.OpenAsync();
        await Task.Delay(HumanPause, Cancel);

        using (var response = await visitor.PostAsync(form, lead))
        {
            (await response.Content.ReadAsStringAsync(Cancel)).ShouldContain("Thanks, we will be in touch.");
        }

        // Invalid and failed posts log too; cover those paths with the same marker.
        using (var invalid = await visitor.PostAsync(form, lead with { Email = $"bad{marker}" }))
        {
            invalid.EnsureSuccessStatusCode();
        }

        var key = (await LeadsAsync()).ShouldHaveSingleItem().IdempotencyKey;
        string[] secrets = [marker, key, visitor.ClientIp, form.RenderToken];

        var logs = await siteLogs.UntilAsync("Lead form post: Invalid");
        // The MAP client's span exists only for the post, so once it is exported the post's spans are there too.
        await TelemetryUntilAsync("/leads");
        await Task.Delay(TimeSpan.FromSeconds(1), Cancel); // one more export interval, for the invalid post's spans
        foreach (var secret in secrets)
        {
            logs.ShouldNotContain(secret, Case.Sensitive, "the site's logs");
            (await TelemetryContainsAsync(secret)).ShouldBeFalse("the site's spans");
        }
    }

    private Visitor Visitor() => new(site);

    private static LeadValues Lead() => new("Test Person", "test@example.com", null, null);

    private async Task ClearLeadsAsync() => (await site.MapStub.DeleteAsync("/stub/leads", Cancel)).EnsureSuccessStatusCode();

    private async Task SetOutageAsync(bool on) => (await site.MapStub.PutAsync($"/stub/outage/{on}", null, Cancel)).EnsureSuccessStatusCode();

    private async Task<List<StoredLead>> LeadsAsync() => (await site.MapStub.GetFromJsonAsync<List<StoredLead>>("/stub/leads", Cancel))!;

    private async Task<bool> TelemetryContainsAsync(string value)
    {
        using var response = await site.MapStub.PostAsync("/stub/telemetry/search", new StringContent(value), Cancel);
        return (await response.Content.ReadFromJsonAsync<TelemetrySearch>(Cancel))!.Found;
    }

    /// <summary>Waits until the spans for this request have been exported, so an absent value is really absent.</summary>
    private async Task TelemetryUntilAsync(string value)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(Cancel);
        deadline.CancelAfter(TimeSpan.FromSeconds(30));
        while (!await TelemetryContainsAsync(value))
        {
            await Task.Delay(250, deadline.Token);
        }
    }

    /// <summary>
    /// Collects the site's console logs from now on. The log stream only starts once someone watches, so the collector
    /// must start before the posts it is meant to see.
    /// </summary>
    private sealed class SiteLogs : IAsyncDisposable
    {
        private readonly System.Text.StringBuilder _lines = new();
        private readonly CancellationTokenSource _stop = new();
        private readonly Task _watching;

        public SiteLogs(SiteFixture site)
        {
            var watcher = site.App.Services.GetRequiredService<ResourceLoggerService>();
            var web = site.App.Services.GetRequiredService<DistributedApplicationModel>().Resources.Single(resource => resource.Name == "web");
            _watching = Task.Run(async () =>
            {
                // By resource, not by name: the stream is kept per running instance.
                await foreach (var batch in watcher.WatchAsync(web).WithCancellation(_stop.Token))
                {
                    lock (_lines)
                    {
                        foreach (var line in batch)
                        {
                            _lines.AppendLine(line.Content);
                        }
                    }
                }
            });
        }

        public string Text
        {
            get
            {
                lock (_lines)
                {
                    return _lines.ToString();
                }
            }
        }

        /// <summary>Waits until <paramref name="expected"/> appears, so an absent value is really absent.</summary>
        public async Task<string> UntilAsync(string expected)
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(Cancel);
            deadline.CancelAfter(TimeSpan.FromSeconds(30));
            while (!Text.Contains(expected, StringComparison.Ordinal))
            {
                if (deadline.IsCancellationRequested)
                {
                    var tail = string.Join('\n', Text.Split('\n').TakeLast(8));
                    throw new ShouldAssertException($"The site never logged \"{expected}\" ({Text.Length} characters captured). Last lines:\n{tail}");
                }

                await Task.Delay(250, CancellationToken.None);
            }

            return Text;
        }

        public async ValueTask DisposeAsync()
        {
            await _stop.CancelAsync();
            try
            {
                await _watching;
            }
            catch (OperationCanceledException)
            {
            }

            _stop.Dispose();
        }
    }

    internal sealed record LeadValues(string? Name, string? Email, string? Company, string? Message);

    private sealed record StoredLead(string IdempotencyKey, string? ListId, string? ConsentVersion, bool MarketingConsent);

    private sealed record TelemetrySearch(int Exports, bool Found);

    /// <summary>A rendered form: its tokens and the page around it.</summary>
    internal sealed partial record RenderedForm(string Html, string RenderToken, string Antiforgery, string RouteToken, string CacheControl)
    {
        public static RenderedForm Parse(string html, HttpResponseMessage response) => new(
            html,
            Hidden(html, "RenderToken"),
            Hidden(html, "__RequestVerificationToken"),
            Hidden(html, "ufprt"),
            response.Headers.CacheControl?.ToString() ?? string.Empty);

        private static string Hidden(string html, string name) =>
            HiddenInput().Matches(html).Select(m => m.Groups)
                .Where(g => g["name"].Value == name).Select(g => WebUtility.HtmlDecode(g["value"].Value))
                .FirstOrDefault() ?? throw new ShouldAssertException($"The page has no {name} field.");

        [GeneratedRegex("<input[^>]*name=\"(?<name>[^\"]+)\"[^>]*value=\"(?<value>[^\"]*)\"")]
        private static partial Regex HiddenInput();
    }
}

/// <summary>One browser: its own cookies and its own client IP, named through the edge header the site trusts in tests.</summary>
internal sealed class Visitor : IDisposable
{
    private readonly HttpClient _client;
    private readonly SiteFixture _site;

    public Visitor(SiteFixture site)
    {
        _site = site;
        ClientIp = RandomIp();
        _client = new HttpClient(new HttpClientHandler { CookieContainer = new CookieContainer(), UseProxy = false, AllowAutoRedirect = false })
        {
            BaseAddress = site.Web.BaseAddress,
            Timeout = TimeSpan.FromSeconds(60),
        };
    }

    public string ClientIp { get; }

    public async Task<LeadFormTests.RenderedForm> OpenAsync()
    {
        using var request = Request(HttpMethod.Get);
        using var response = await _client.SendAsync(request, TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
        return LeadFormTests.RenderedForm.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken), response);
    }

    public Task<HttpResponseMessage> PostAsync(LeadFormTests.RenderedForm form, LeadFormTests.LeadValues lead, params (string Name, string Value)[] extra) =>
        PostAsync(form, lead, antiforgery: true, extra: extra);

    public Task<HttpResponseMessage> PostAsync(
        LeadFormTests.RenderedForm form,
        LeadFormTests.LeadValues lead,
        bool antiforgery = true,
        string path = "/fixture-form/",
        bool chunked = false,
        params (string Name, string Value)[] extra)
    {
        var fields = new List<KeyValuePair<string, string>>
        {
            new("RenderToken", form.RenderToken),
            new("ufprt", form.RouteToken),
            new("ConsentVersion", "2026-11"),
        };
        if (antiforgery)
        {
            fields.Add(new("__RequestVerificationToken", form.Antiforgery));
        }

        void Add(string name, string? value)
        {
            if (value is not null)
            {
                fields.Add(new(name, value));
            }
        }

        Add("Name", lead.Name);
        Add("Email", lead.Email);
        Add("Company", lead.Company);
        Add("Message", lead.Message);
        fields.AddRange(extra.Select(e => new KeyValuePair<string, string>(e.Name, e.Value)));

        var request = Request(HttpMethod.Post, path);
        request.Content = new FormUrlEncodedContent(fields);
        if (chunked)
        {
            request.Headers.TransferEncodingChunked = true;
            request.Content = new UnknownLengthContent(request.Content);
        }

        return _client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    public void Dispose() => _client.Dispose();

    private HttpRequestMessage Request(HttpMethod method, string path = "/fixture-form/")
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Add("X-Azure-FDID", _site.FrontDoorId);
        request.Headers.Add("X-Azure-ClientIP", ClientIp);
        return request;
    }

    // A random private address, so no two visitors in a run share a rate-limit window.
    private static string RandomIp() => $"10.{Random.Shared.Next(0, 256)}.{Random.Shared.Next(0, 256)}.{Random.Shared.Next(1, 255)}";
}

/// <summary>Sends a body without a Content-Length, so it goes chunked and the server cannot refuse it up front.</summary>
internal sealed class UnknownLengthContent : HttpContent
{
    private readonly HttpContent _inner;

    public UnknownLengthContent(HttpContent inner)
    {
        _inner = inner;
        Headers.ContentType = inner.Headers.ContentType;
    }

    protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) => _inner.CopyToAsync(stream);

    protected override bool TryComputeLength(out long length)
    {
        length = 0;
        return false;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _inner.Dispose();
        }

        base.Dispose(disposing);
    }
}
