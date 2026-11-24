using Shouldly;
using UmbracoSample.Web.Components;

namespace UmbracoSample.Web.Tests.Components;

/// <summary>
/// Contract tests for every component with empty, minimal and maximal content (TST-05, ES-001).
/// The same three states exist as seed pages (/fixture-empty/, /fixture-minimal/, /fixture-maximal/),
/// which the integration tests render through the real stack.
/// </summary>
public sealed class ComponentContractTests
{
    private static readonly LinkModel SomeLink = new("Read more", "/about/", OpensInNewTab: false);
    private static readonly ImageModel SomeImage = new("/media/hero.jpg?width=1600", "A described image");

    [Theory]
    [InlineData(ComponentMapper.HeroAlias)]
    [InlineData(ComponentMapper.RichTextAlias)]
    [InlineData(ComponentMapper.CardGridAlias)]
    [InlineData(ComponentMapper.CtaAlias)]
    public void Empty_block_is_not_rendered(string alias) =>
        ComponentMapper.Map(new FakeBlockSource(alias)).ShouldBeNull();

    [Fact]
    public void Unknown_block_type_is_not_rendered() =>
        ComponentMapper.Map(new FakeBlockSource("somethingNew").With("heading", "Hello")).ShouldBeNull();

    [Fact]
    public void Hero_minimal_has_only_its_heading()
    {
        var hero = ComponentMapper.Map(new FakeBlockSource(ComponentMapper.HeroAlias).With("heading", "  Welcome  "))
            .ShouldBeOfType<HeroModel>();

        hero.ShouldBe(new HeroModel("Welcome", Intro: null, Image: null, Link: null));
    }

    [Fact]
    public void Hero_maximal_carries_every_field()
    {
        var hero = ComponentMapper.Map(new FakeBlockSource(ComponentMapper.HeroAlias)
                .With("heading", "Welcome")
                .With("intro", "Intro text")
                .With("image", SomeImage)
                .With("link", SomeLink))
            .ShouldBeOfType<HeroModel>();

        hero.ShouldBe(new HeroModel("Welcome", "Intro text", SomeImage, SomeLink));
    }

    [Fact]
    public void Hero_with_blank_heading_is_not_rendered() =>
        ComponentMapper.Map(new FakeBlockSource(ComponentMapper.HeroAlias).With("heading", "   ").With("intro", "Intro"))
            .ShouldBeNull();

    [Fact]
    public void Rich_text_minimal_and_maximal_keep_the_markup()
    {
        const string markup = "<h2>Heading</h2><p>Text with <a href=\"/x\">a link</a>.</p>";

        var text = ComponentMapper.Map(new FakeBlockSource(ComponentMapper.RichTextAlias).With("text", markup))
            .ShouldBeOfType<RichTextModel>();

        text.Html.ToString().ShouldBe(markup);
    }

    [Fact]
    public void Rich_text_with_only_whitespace_is_not_rendered() =>
        ComponentMapper.Map(new FakeBlockSource(ComponentMapper.RichTextAlias).With("text", "  ")).ShouldBeNull();

    [Fact]
    public void Card_grid_minimal_has_one_card_and_level_two_card_titles()
    {
        var grid = ComponentMapper.Map(new FakeBlockSource(ComponentMapper.CardGridAlias)
                .With("cards", Cards(Card("Only card"))))
            .ShouldBeOfType<CardGridModel>();

        grid.Heading.ShouldBeNull();
        grid.Cards.ShouldBe([new CardModel("Only card", Text: null, Link: null)]);
        grid.CardHeadingLevel.ShouldBe(2, "without a grid heading, card titles are the next level below the page h1");
    }

    [Fact]
    public void Card_grid_maximal_keeps_card_order_and_uses_level_three_titles()
    {
        var grid = ComponentMapper.Map(new FakeBlockSource(ComponentMapper.CardGridAlias)
                .With("heading", "Cards")
                .With("cards", Cards(
                    Card("One").With("text", "First").With("link", SomeLink),
                    Card("Two"),
                    Card("Three").With("text", "Third"))))
            .ShouldBeOfType<CardGridModel>();

        grid.Heading.ShouldBe("Cards");
        grid.Cards.Select(card => card.Title).ShouldBe(["One", "Two", "Three"]);
        grid.Cards[0].ShouldBe(new CardModel("One", "First", SomeLink));
        grid.CardHeadingLevel.ShouldBe(3);
    }

    [Fact]
    public void Card_grid_drops_invalid_cards_and_is_not_rendered_when_none_remain()
    {
        var mixed = ComponentMapper.Map(new FakeBlockSource(ComponentMapper.CardGridAlias)
                .With("cards", Cards(Card(" "), Card("Valid"), new FakeBlockSource("heroBlock").With("title", "Wrong type"))))
            .ShouldBeOfType<CardGridModel>();
        mixed.Cards.Select(card => card.Title).ShouldBe(["Valid"]);

        ComponentMapper.Map(new FakeBlockSource(ComponentMapper.CardGridAlias)
                .With("heading", "Cards")
                .With("cards", Cards(Card(""))))
            .ShouldBeNull();
    }

    [Fact]
    public void Call_to_action_minimal_needs_heading_and_link()
    {
        var cta = ComponentMapper.Map(new FakeBlockSource(ComponentMapper.CtaAlias).With("heading", "Act").With("link", SomeLink))
            .ShouldBeOfType<CtaModel>();

        cta.ShouldBe(new CtaModel("Act", Text: null, SomeLink));
    }

    [Fact]
    public void Call_to_action_maximal_carries_its_text()
    {
        var cta = ComponentMapper.Map(new FakeBlockSource(ComponentMapper.CtaAlias)
                .With("heading", "Act")
                .With("text", "Why")
                .With("link", SomeLink))
            .ShouldBeOfType<CtaModel>();

        cta.Text.ShouldBe("Why");
    }

    [Fact]
    public void Call_to_action_without_a_link_is_not_rendered() =>
        ComponentMapper.Map(new FakeBlockSource(ComponentMapper.CtaAlias).With("heading", "Act")).ShouldBeNull();

    private static FakeBlockSource Card(string title) => new FakeBlockSource(ComponentMapper.CardAlias).With("title", title);

    private static List<IBlockSource> Cards(params IBlockSource[] cards) => [.. cards];
}
