using Microsoft.AspNetCore.Html;
using UmbracoSample.Components;

namespace UmbracoSample.Web.Tests.Components;

/// <summary>A behavioural fake of a block: values keyed by property alias (ES-004).</summary>
internal sealed class FakeBlockSource(string contentTypeAlias) : IBlockSource
{
    private readonly Dictionary<string, object> _values = new(StringComparer.Ordinal);

    public Guid Key { get; init; } = Guid.NewGuid();

    public string ContentTypeAlias { get; } = contentTypeAlias;

    public FakeBlockSource With(string propertyAlias, object value)
    {
        _values[propertyAlias] = value;
        return this;
    }

    public string? Text(string propertyAlias) => _values.GetValueOrDefault(propertyAlias) as string;

    public IHtmlContent? Html(string propertyAlias) =>
        _values.GetValueOrDefault(propertyAlias) is string html ? new HtmlString(html) : null;

    public LinkModel? Link(string propertyAlias) => _values.GetValueOrDefault(propertyAlias) as LinkModel;

    public ImageModel? Image(string propertyAlias) => _values.GetValueOrDefault(propertyAlias) as ImageModel;

    public IReadOnlyList<IBlockSource> Blocks(string propertyAlias) =>
        _values.GetValueOrDefault(propertyAlias) as IReadOnlyList<IBlockSource> ?? [];
}
