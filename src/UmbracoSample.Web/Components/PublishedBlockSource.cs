using Microsoft.AspNetCore.Html;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Models.Blocks;
using Umbraco.Cms.Core.Models.PublishedContent;
using Umbraco.Cms.Core.Strings;

namespace UmbracoSample.Web.Components;

/// <summary>Reads block values from published Umbraco content. Covered by the integration tests against the fixtures.</summary>
public sealed class PublishedBlockSource(IPublishedElement element) : IBlockSource
{
    /// <summary>Hero images are requested at this width; ImageSharp resizes and caches them.</summary>
    public const int ImageWidth = 1600;

    public string ContentTypeAlias => element.ContentType.Alias;

    public string? Text(string propertyAlias) => Value(propertyAlias) as string;

    public IHtmlContent? Html(string propertyAlias) =>
        Value(propertyAlias) is IHtmlEncodedString html ? new HtmlString(html.ToHtmlString()) : null;

    public LinkModel? Link(string propertyAlias)
    {
        var link = Value(propertyAlias) switch
        {
            Link single => single,
            IEnumerable<Link> many => many.FirstOrDefault(),
            _ => null,
        };

        return link is null ? null : LinkModel.TryCreate(link.Name, link.Url, link.Target);
    }

    public ImageModel? Image(string propertyAlias)
    {
        var media = Value(propertyAlias) switch
        {
            MediaWithCrops single => single,
            IEnumerable<MediaWithCrops> many => many.FirstOrDefault(),
            _ => null,
        };

        if (media is null)
        {
            return null;
        }

        var url = media.GetCropUrl(width: ImageWidth) ?? media.Url();
        var alt = media.Content.GetProperty("altText")?.GetValue() as string;
        return ImageModel.TryCreate(url, alt);
    }

    public IReadOnlyList<IBlockSource> Blocks(string propertyAlias) =>
        Value(propertyAlias) is BlockListModel blocks
            ? [.. blocks.Select(item => new PublishedBlockSource(item.Content))]
            : [];

    private object? Value(string propertyAlias) => element.GetProperty(propertyAlias)?.GetValue();
}

/// <summary>Builds the component models for a page's "blocks" property.</summary>
public sealed partial class PageComponents(ILogger<PageComponents> logger)
{
    public const string BlocksAlias = "blocks";

    public IReadOnlyList<IComponentModel> For(IPublishedContent page)
    {
        ArgumentNullException.ThrowIfNull(page);

        if (page.GetProperty(BlocksAlias)?.GetValue() is not BlockListModel blocks)
        {
            return [];
        }

        var models = new List<IComponentModel>(blocks.Count);
        foreach (var block in blocks)
        {
            if (ComponentMapper.Map(new PublishedBlockSource(block.Content)) is { } model)
            {
                models.Add(model);
            }
            else
            {
                // A skipped block is an editor problem, not a failure: log enough to find it, never content values (ES-025).
                LogSkippedBlock(logger, block.Content.Key, block.Content.ContentType.Alias, page.Key);
            }
        }

        return models;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Skipped block {BlockKey} of type {BlockType} on page {PageKey}: required values are missing or the type has no renderer.")]
    private static partial void LogSkippedBlock(ILogger logger, Guid blockKey, string blockType, Guid pageKey);
}
