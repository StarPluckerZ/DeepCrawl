using DeepCrawl.Domain.Abstractions;
using Microsoft.Extensions.Logging;
using ReverseMarkdown;

namespace DeepCrawl.Infrastructure.Cleaning;

public class ReverseMarkdownConverter : IMarkdownConverter
{
    private readonly ILogger<ReverseMarkdownConverter> _logger;

    public ReverseMarkdownConverter(ILogger<ReverseMarkdownConverter> logger)
    {
        _logger = logger;
    }

    public string Convert(string cleanHtml)
    {
        if (string.IsNullOrWhiteSpace(cleanHtml))
            return string.Empty;

        var config = new Config
        {
            GithubFlavored = true,
            RemoveComments = true,
            TableWithoutHeaderRowHandling = Config.TableWithoutHeaderRowHandlingOption.Default,
        };

        var converter = new ReverseMarkdown.Converter(config);
        var markdown = converter.Convert(cleanHtml);

        markdown = System.Text.RegularExpressions.Regex.Replace(markdown, @"\n{3,}", "\n\n");
        markdown = markdown.Trim();

        _logger.LogInformation("Markdown converted: {InputLen} -> {OutputLen} chars", cleanHtml.Length, markdown.Length);
        return markdown;
    }
}
