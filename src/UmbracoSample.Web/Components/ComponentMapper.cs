using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Html;

namespace UmbracoSample.Web.Components;

/// <summary>Reads one block's values. Implemented over Umbraco content at runtime and by fakes in contract tests.</summary>
public interface IBlockSource
{
    string ContentTypeAlias { get; }

    string? Text(string propertyAlias);

    IHtmlContent? Html(string propertyAlias);

    LinkModel? Link(string propertyAlias);

    ImageModel? Image(string propertyAlias);

    IReadOnlyList<IBlockSource> Blocks(string propertyAlias);
}

/// <summary>
/// Maps blocks to component models. A block whose required values are missing maps to null and is not rendered,
/// so a half-filled block never produces broken markup (TST-05 contract: empty, minimal and maximal content).
/// </summary>
public static partial class ComponentMapper
{
    public const string HeroAlias = "heroBlock";
    public const string RichTextAlias = "richTextBlock";
    public const string CardGridAlias = "cardGridBlock";
    public const string CardAlias = "cardBlock";
    public const string CtaAlias = "ctaBlock";

    public static IComponentModel? Map(IBlockSource block)
    {
        ArgumentNullException.ThrowIfNull(block);

        return block.ContentTypeAlias switch
        {
            HeroAlias => Hero(block),
            RichTextAlias => RichText(block),
            CardGridAlias => CardGrid(block),
            CtaAlias => Cta(block),
            _ => null,
        };
    }

    private static HeroModel? Hero(IBlockSource block) =>
        Required(block.Text("heading")) is { } heading
            ? new HeroModel(heading, Optional(block.Text("intro")), block.Image("image"), block.Link("link"))
            : null;

    private static RichTextModel? RichText(IBlockSource block) =>
        block.Html("text") is { } html && HasVisibleText(html.ToString() ?? string.Empty)
            ? new RichTextModel(html)
            : null;

    /// <summary>An emptied editor leaves markup such as <c>&lt;p&gt;&lt;/p&gt;</c>; that is not content.</summary>
    internal static bool HasVisibleText(string html) =>
        !string.IsNullOrWhiteSpace(Tags().Replace(html, string.Empty).Replace("&nbsp;", " ", StringComparison.OrdinalIgnoreCase));

    private static CardGridModel? CardGrid(IBlockSource block)
    {
        var cards = block.Blocks("cards")
            .Where(card => card.ContentTypeAlias == CardAlias)
            .Select(Card)
            .OfType<CardModel>()
            .ToList();

        return cards.Count == 0 ? null : new CardGridModel(Optional(block.Text("heading")), cards);
    }

    private static CardModel? Card(IBlockSource block) =>
        Required(block.Text("title")) is { } title
            ? new CardModel(title, Optional(block.Text("text")), block.Link("link"))
            : null;

    private static CtaModel? Cta(IBlockSource block) =>
        Required(block.Text("heading")) is { } heading && block.Link("link") is { } link
            ? new CtaModel(heading, Optional(block.Text("text")), link)
            : null;

    private static string? Required(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? Optional(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    [GeneratedRegex("<[^>]*>")]
    private static partial Regex Tags();
}
