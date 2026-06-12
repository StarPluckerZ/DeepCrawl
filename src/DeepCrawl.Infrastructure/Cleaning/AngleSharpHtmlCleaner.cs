using AngleSharp;
using AngleSharp.Dom;
using AngleSharp.Html.Dom;
using DeepCrawl.Domain.Abstractions;
using Microsoft.Extensions.Logging;

namespace DeepCrawl.Infrastructure.Cleaning;

public class AngleSharpHtmlCleaner : IHtmlCleaner
{
    private static readonly string[] RemoveTags =
    [
        "script", "style", "nav", "footer", "header", "aside", "noscript",
        "iframe", "form", "img", "video", "audio", "svg", "canvas",
        "input", "button", "select", "textarea", "fieldset", "figure",
        "picture", "source", "map", "area"
    ];

    private static readonly string[] RemoveAttributes =
    [
        "[role=\"navigation\"]", "[role=\"banner\"]", "[aria-hidden=\"true\"]",
        "[role=\"complementary\"]"
    ];

    private readonly ILogger<AngleSharpHtmlCleaner> _logger;

    public AngleSharpHtmlCleaner(ILogger<AngleSharpHtmlCleaner> logger)
    {
        _logger = logger;
    }

    public async Task<string> CleanAsync(string rawHtml)
    {
        if (string.IsNullOrWhiteSpace(rawHtml))
            return string.Empty;

        var config = Configuration.Default;
        var context = BrowsingContext.New(config);
        var document = await context.OpenAsync(req => req.Content(rawHtml));

        CleanNode(document.Body ?? throw new InvalidOperationException("No body element found"));

        var result = document.Body.InnerHtml;
        _logger.LogInformation("HTML cleaned: {InputLen} -> {OutputLen} chars", rawHtml.Length, result.Length);
        return result;
    }

    private static void CleanNode(INode node)
    {
        var children = node.ChildNodes.ToList();

        foreach (var child in children)
        {
            if (child is IElement element)
            {
                if (ShouldRemoveTag(element))
                {
                    child.RemoveFromParent();
                    continue;
                }

                if (element.TagName.Equals("A", StringComparison.OrdinalIgnoreCase))
                {
                    UnwrapAnchor(element);
                    continue;
                }

                if (IsHidden(element))
                {
                    child.RemoveFromParent();
                    continue;
                }

                CleanAttributes(element);
                CleanNode(child);
            }
        }

        RemoveEmptyContainers(node);
    }

    private static bool ShouldRemoveTag(IElement element)
    {
        var tag = element.TagName.ToLowerInvariant();

        if (RemoveTags.Contains(tag))
            return true;

        foreach (var attr in RemoveAttributes)
        {
            if (element.Matches(attr))
                return true;
        }

        return false;
    }

    private static bool IsHidden(IElement element)
    {
        var style = element.GetAttribute("style") ?? "";
        if (style.Contains("display:none", StringComparison.OrdinalIgnoreCase) ||
            style.Contains("display: none", StringComparison.OrdinalIgnoreCase))
            return true;

        var className = element.ClassName ?? "";
        if (className is not null &&
            (className.Contains("hidden", StringComparison.OrdinalIgnoreCase) ||
             className.Contains("hide", StringComparison.OrdinalIgnoreCase)))
            return true;

        return false;
    }

    private static void CleanAttributes(IElement element)
    {
        var attrsToRemove = new List<IAttr>();
        foreach (var attr in element.Attributes)
        {
            var name = attr.Name.ToLowerInvariant();
            if (name is "style" or "class" or "id" or "onclick" or "onload" ||
                name.StartsWith("on") || name.StartsWith("data-") ||
                name is "aria-hidden" or "role" or "tabindex" or "target" or "rel")
            {
                attrsToRemove.Add(attr);
            }
        }
        foreach (var attr in attrsToRemove)
            element.RemoveAttribute(attr.Name);
    }

    private static void UnwrapAnchor(IElement anchor)
    {
        var parent = anchor.ParentElement;
        if (parent is null) return;

        var children = anchor.ChildNodes.ToList();

        foreach (var child in children)
        {
            anchor.RemoveChild(child);
            parent.InsertBefore(child, anchor);
            CleanNode(child);
        }

        anchor.RemoveFromParent();
    }

    private static void RemoveEmptyContainers(INode node)
    {
        var children = node.ChildNodes.ToList();
        foreach (var child in children)
        {
            if (child is IElement element)
            {
                var tag = element.TagName.ToLowerInvariant();

                if (tag is "div" or "span" or "p" or "section" or "article" or "li" or "ul" or "ol")
                {
                    var text = element.TextContent.Trim();
                    if (string.IsNullOrEmpty(text))
                    {
                        child.RemoveFromParent();
                    }
                    else
                    {
                        RemoveEmptyContainers(child);
                    }
                }
            }
        }
    }
}
