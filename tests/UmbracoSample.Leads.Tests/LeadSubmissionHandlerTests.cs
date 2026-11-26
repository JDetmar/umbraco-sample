using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using UmbracoSample.Domain.Leads;

namespace UmbracoSample.Leads.Tests;

/// <summary>
/// Threat model 0001: the order of checks, that the list comes from the block and never the request, that consent
/// decides a marketing list, and how MAP outcomes become what the visitor is told.
/// </summary>
public sealed class LeadSubmissionHandlerTests
{
    private static readonly Guid Block = Guid.Parse("0f8fad5b-d9cb-469f-a165-70867728950e");
    private static readonly PlatformList Newsletter = new("newsletter", "list-news", ListPurpose.Marketing);
    private static readonly PlatformList Sales = new("sales", "list-sales", ListPurpose.Sales);

    private readonly TestTime _time = new(new DateTimeOffset(2026, 11, 26, 9, 0, 0, TimeSpan.Zero));
    private readonly RenderTokenProtector _tokens;
    private readonly FakeMap _map = new();
    private readonly FakeCatalog _catalog = new();

    public LeadSubmissionHandlerTests()
    {
        _tokens = new RenderTokenProtector(new EphemeralDataProtectionProvider(), _time);
        _catalog.Forms[Block] = new LeadFormSettings(Sales, new HashSet<string>(["2026-11"], StringComparer.Ordinal));
    }

    private static LeadInput Valid(bool consent = false) => new("Ada Lovelace", "ada@example.com", null, "Hello", consent, "2026-11");

    private LeadSubmissionHandler Handler(bool captchaOn = false, ICaptchaVerifier? verifier = null) => new(
        _tokens,
        _catalog,
        _map,
        Options.Create(new LeadFormOptions()),
        Options.Create(new CaptchaOptions { Enabled = captchaOn }),
        verifier is null ? [] : [verifier],
        _time,
        NullLogger<LeadSubmissionHandler>.Instance);

    private async Task<LeadFormResult> Submit(LeadInput input, string? honeypot = null, TimeSpan? after = null, string? token = null, LeadSubmissionHandler? handler = null)
    {
        token ??= _tokens.Issue(Block);
        _time.Advance(after ?? TimeSpan.FromSeconds(20));
        return await (handler ?? Handler()).HandleAsync(new LeadFormPost(token, honeypot, null, input), TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task A_valid_post_goes_to_the_map_with_the_tokens_key_and_says_thank_you()
    {
        var token = _tokens.Issue(Block);
        _tokens.TryRead(token, out var read).ShouldBeTrue();

        (await Submit(Valid(), token: token)).Kind.ShouldBe(LeadFormResultKind.ThankYou);

        var sent = _map.Sent.ShouldHaveSingleItem();
        sent.IdempotencyKey.ShouldBe(read.IdempotencyKey);
        sent.ListId.ShouldBe("list-sales");
        sent.SubmittedAt.ShouldBe(_time.Now);
    }

    [Theory]
    [InlineData(true, "list-news")]
    [InlineData(false, null)]
    public async Task A_marketing_block_gets_the_lead_only_with_consent(bool consent, string? expected)
    {
        _catalog.Forms[Block] = _catalog.Forms[Block] with { List = Newsletter };

        (await Submit(Valid(consent))).Kind.ShouldBe(LeadFormResultKind.ThankYou);

        _map.Sent.ShouldHaveSingleItem().ListId.ShouldBe(expected);
    }

    [Fact]
    public async Task The_post_cannot_name_a_list()
    {
        // LeadFormPost has no list field at all: the list is the published block's, resolved through the allowlist.
        typeof(LeadFormPost).GetProperties().Select(p => p.Name).ShouldNotContain(n => n.Contains("List", StringComparison.OrdinalIgnoreCase));
        typeof(LeadInput).GetProperties().Select(p => p.Name).ShouldNotContain(n => n.Contains("List", StringComparison.OrdinalIgnoreCase));
        (await Submit(Valid())).Kind.ShouldBe(LeadFormResultKind.ThankYou);
        _map.Sent.ShouldHaveSingleItem().ListId.ShouldBe("list-sales");
    }

    [Fact]
    public async Task A_filled_honeypot_is_refused_without_calling_the_map()
    {
        (await Submit(Valid(), honeypot: "http://spam.example")).Kind.ShouldBe(LeadFormResultKind.Refused);
        _map.Sent.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(2)]
    [InlineData(2 * 60 * 60 + 1)]
    public async Task A_post_too_soon_or_too_late_after_rendering_is_refused(int seconds)
    {
        (await Submit(Valid(), after: TimeSpan.FromSeconds(seconds))).Kind.ShouldBe(LeadFormResultKind.Refused);
        _map.Sent.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("tampered")]
    public async Task A_missing_or_tampered_token_is_refused(string? token)
    {
        (await Submit(Valid(), token: token ?? string.Empty)).Kind.ShouldBe(LeadFormResultKind.Refused);
        _map.Sent.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_token_for_a_block_that_is_no_longer_published_is_refused()
    {
        _catalog.Forms.Clear();

        (await Submit(Valid())).Kind.ShouldBe(LeadFormResultKind.Refused);
        _map.Sent.ShouldBeEmpty();
    }

    [Fact]
    public async Task Invalid_fields_are_reported_per_field_and_nothing_is_sent()
    {
        var result = await Submit(Valid() with { Email = "not an email", ConsentVersion = "v999" });

        result.Kind.ShouldBe(LeadFormResultKind.Invalid);
        result.Errors.Keys.ShouldBe(["email", "consentVersion"], ignoreOrder: true);
        _map.Sent.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(MapOutcome.Accepted, LeadFormResultKind.ThankYou)]
    [InlineData(MapOutcome.Rejected, LeadFormResultKind.NotAccepted)]
    [InlineData(MapOutcome.Failed, LeadFormResultKind.TryAgain)]
    [InlineData(MapOutcome.Overloaded, LeadFormResultKind.TryAgain)]
    [InlineData(MapOutcome.Unknown, LeadFormResultKind.OutcomeUnknown)]
    public async Task Only_an_accepted_lead_is_thanked(MapOutcome outcome, LeadFormResultKind expected)
    {
        _map.Outcome = outcome;

        (await Submit(Valid())).Kind.ShouldBe(expected);
    }

    [Fact]
    public async Task With_the_captcha_off_no_verifier_is_needed() =>
        (await Submit(Valid(), handler: Handler(captchaOn: false))).Kind.ShouldBe(LeadFormResultKind.ThankYou);

    [Theory]
    [InlineData(true, LeadFormResultKind.ThankYou)]
    [InlineData(false, LeadFormResultKind.Refused)]
    public async Task With_the_captcha_on_the_response_is_verified_server_side(bool passes, LeadFormResultKind expected)
    {
        var verifier = new FakeVerifier(passes);

        (await Submit(Valid(), handler: Handler(captchaOn: true, verifier))).Kind.ShouldBe(expected);
        verifier.Calls.ShouldBe(1);
    }

    private sealed class FakeMap : IMapClient
    {
        public List<MapSubmission> Sent { get; } = [];

        public MapOutcome Outcome { get; set; } = MapOutcome.Accepted;

        public Task<MapOutcome> SubmitAsync(MapSubmission submission, CancellationToken cancellationToken)
        {
            Sent.Add(submission);
            return Task.FromResult(Outcome);
        }
    }

    private sealed class FakeCatalog : ILeadFormCatalog
    {
        public Dictionary<Guid, LeadFormSettings> Forms { get; } = [];

        public LeadFormSettings? Find(Guid blockKey) => Forms.GetValueOrDefault(blockKey);
    }

    private sealed class FakeVerifier(bool passes) : ICaptchaVerifier
    {
        public int Calls { get; private set; }

        public Task<bool> VerifyAsync(string? response, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(passes);
        }
    }
}
