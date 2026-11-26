using Microsoft.AspNetCore.Html;
using Microsoft.Extensions.Logging;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Models.Blocks;
using Umbraco.Cms.Core.Models.PublishedContent;
using Umbraco.Cms.Core.Strings;
using Umbraco.Extensions;

namespace UmbracoSample.Components;

/// <summary>Reads block values from published Umbraco content. Covered by the integration tests against the fixtures.</summary>
public sealed partial class PublishedBlockSource(IPublishedElement element, ILogger logger) : IBlockSource
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

        if (link is null)
        {
            return null;
        }

        var model = LinkModel.TryCreate(link.Name, link.Url, link.Target);
        if (model is null)
        {
            LogRejectedLink(logger, element.Key, propertyAlias);
        }

        return model;
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
        var image = ImageModel.TryCreate(url, alt);
        if (image is null)
        {
            LogImageWithoutAltText(logger, element.Key, media.Content.Key);
        }

        return image;
    }

    public IReadOnlyList<IBlockSource> Blocks(string propertyAlias) =>
        Value(propertyAlias) is BlockListModel blocks
            ? [.. blocks.Select(item => new PublishedBlockSource(item.Content, logger))]
            : [];

    private object? Value(string propertyAlias) => element.GetProperty(propertyAlias)?.GetValue();

    // Keys and aliases only, never URLs or content (ES-025).
    [LoggerMessage(Level = LogLevel.Warning, Message = "Dropped the link in {PropertyAlias} on block {BlockKey}: its URL is not an allowed scheme or site-relative path.")]
    private static partial void LogRejectedLink(ILogger logger, Guid blockKey, string propertyAlias);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Did not render image {MediaKey} on block {BlockKey}: the media item has no alt text.")]
    private static partial void LogImageWithoutAltText(ILogger logger, Guid blockKey, Guid mediaKey);
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
            if (ComponentMapper.Map(new PublishedBlockSource(block.Content, logger)) is { } model)
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
