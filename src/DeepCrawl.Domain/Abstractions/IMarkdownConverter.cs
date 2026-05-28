namespace DeepCrawl.Domain.Abstractions;

public interface IMarkdownConverter
{
    string Convert(string cleanHtml);
}
