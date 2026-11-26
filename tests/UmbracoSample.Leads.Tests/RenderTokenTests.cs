using Microsoft.AspNetCore.DataProtection;
using Shouldly;

namespace UmbracoSample.Leads.Tests;

/// <summary>Threat model 0001: the render token cannot be read, forged or changed, and each render gets its own key.</summary>
public sealed class RenderTokenTests
{
    private static readonly Guid Block = Guid.Parse("0f8fad5b-d9cb-469f-a165-70867728950e");
    private readonly TestTime _time = new(new DateTimeOffset(2026, 11, 26, 9, 0, 0, TimeSpan.Zero));
    private readonly EphemeralDataProtectionProvider _keys = new();

    [Fact]
    public void A_token_reads_back_the_block_and_render_time()
    {
        var protector = new RenderTokenProtector(_keys, _time);

        protector.TryRead(protector.Issue(Block), out var token).ShouldBeTrue();

        token.BlockKey.ShouldBe(Block);
        token.RenderedAt.ShouldBe(_time.Now);
        token.IdempotencyKey.Length.ShouldBe(22);
    }

    [Fact]
    public void Every_render_gets_its_own_idempotency_key()
    {
        var protector = new RenderTokenProtector(_keys, _time);

        var keys = Enumerable.Range(0, 100).Select(_ => protector.TryRead(protector.Issue(Block), out var t) ? t.IdempotencyKey : null).ToList();

        keys.ShouldAllBe(k => k != null);
        keys.Distinct(StringComparer.Ordinal).Count().ShouldBe(100);
    }

    [Fact]
    public void A_changed_token_does_not_read()
    {
        var protector = new RenderTokenProtector(_keys, _time);
        var token = protector.Issue(Block);

        for (var i = 0; i < token.Length; i += 7)
        {
            var changed = token.ToCharArray();
            changed[i] = changed[i] == 'A' ? 'B' : 'A';
            protector.TryRead(new string(changed), out _).ShouldBeFalse($"position {i}");
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not a token")]
    [InlineData("%%%")]
    public void Garbage_does_not_read(string? token) =>
        new RenderTokenProtector(_keys, _time).TryRead(token, out _).ShouldBeFalse();

    [Fact]
    public void An_overlong_token_is_not_even_decoded() =>
        new RenderTokenProtector(_keys, _time).TryRead(new string('A', 513), out _).ShouldBeFalse();

    [Fact]
    public void A_token_from_another_key_ring_or_purpose_does_not_read()
    {
        var other = new RenderTokenProtector(new EphemeralDataProtectionProvider(), _time);
        new RenderTokenProtector(_keys, _time).TryRead(other.Issue(Block), out _).ShouldBeFalse();

        var otherPurpose = Microsoft.AspNetCore.WebUtilities.WebEncoders.Base64UrlEncode(_keys.CreateProtector("something-else").Protect(new byte[40]));
        new RenderTokenProtector(_keys, _time).TryRead(otherPurpose, out _).ShouldBeFalse();
    }
}
