using DeepCrawl.Domain.Abstractions;
using DeepCrawl.Domain.Enums;
using DeepCrawl.Infrastructure.Cleaning;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DeepCrawl.Infrastructure.Tests;

public class MetadataExtractorStepTests
{
    private readonly MetadataExtractorStep _step = new(NullLoggerFactory.Instance.CreateLogger<MetadataExtractorStep>());

    private static CleanContext CreateContext(string url = "https://example.com")
        => new() { Url = url };

    [Fact]
    public async Task Extracts_Robots_From_Meta_Name_In_Head()
    {
        var html = "<html><head><meta name=\"robots\" content=\"noindex, nofollow\"></head><body></body></html>";
        var context = CreateContext();

        var result = await _step.CleanAsync(html, context);

        Assert.NotNull(context.Metadata);
        Assert.Equal("noindex, nofollow", context.Metadata!.Robots);
    }

    [Fact]
    public async Task Extracts_Robots_From_Meta_Property_In_Head()
    {
        var html = "<html><head><meta property=\"robots\" content=\"noarchive\"></head><body></body></html>";
        var context = CreateContext();

        var result = await _step.CleanAsync(html, context);

        Assert.NotNull(context.Metadata);
        Assert.Equal("noarchive", context.Metadata!.Robots);
    }

    [Fact]
    public async Task Name_Robots_Takes_Priority_Over_Property()
    {
        var html = "<html><head><meta name=\"robots\" content=\"name-val\"><meta property=\"robots\" content=\"prop-val\"></head><body></body></html>";
        var context = CreateContext();

        var result = await _step.CleanAsync(html, context);

        Assert.NotNull(context.Metadata);
        Assert.Equal("name-val", context.Metadata!.Robots);
    }

    [Fact]
    public async Task Fallback_To_Body_When_No_Head()
    {
        var html = "<html><body><meta name=\"robots\" content=\"index, follow\"></body></html>";
        var context = CreateContext();

        var result = await _step.CleanAsync(html, context);

        Assert.NotNull(context.Metadata);
        Assert.Equal("index, follow", context.Metadata!.Robots);
    }

    [Fact]
    public async Task Fallback_To_Full_Document_When_Meta_In_Body()
    {
        var html = "<html><head></head><body><meta name=\"robots\" content=\"noodp\"></body></html>";
        var context = CreateContext();

        var result = await _step.CleanAsync(html, context);

        Assert.NotNull(context.Metadata);
        Assert.Equal("noodp", context.Metadata!.Robots);
    }

    [Fact]
    public async Task Returns_Null_When_No_Robots_Tag()
    {
        var html = "<html><head><title>Hello</title></head><body><p>No robots here</p></body></html>";
        var context = CreateContext();

        var result = await _step.CleanAsync(html, context);

        Assert.NotNull(context.Metadata);
        Assert.Null(context.Metadata!.Robots);
    }

    [Fact]
    public async Task Returns_Null_When_Meta_Robots_Has_Empty_Content()
    {
        var html = "<html><head><meta name=\"robots\" content=\"\"></head><body></body></html>";
        var context = CreateContext();

        var result = await _step.CleanAsync(html, context);

        Assert.NotNull(context.Metadata);
        Assert.Null(context.Metadata!.Robots);
    }

    [Fact]
    public async Task Gracefully_Degrades_On_Invalid_Input()
    {
        var html = "not valid html \x00\x01\x02";
        var context = CreateContext();

        var result = await _step.CleanAsync(html, context);

        // Metadata should still be set on context (graceful degradation)
        Assert.NotNull(context.Metadata);
        Assert.Equal("https://example.com", context.Metadata!.SourceURL);
        // Robots should be null since parsing failed
        Assert.Null(context.Metadata!.Robots);
    }

    [Fact]
    public async Task Populates_Other_Metadata_Alongside_Robots()
    {
        var html = "<html lang=\"en\"><head><title>Test Page</title><meta name=\"description\" content=\"A test\"><meta name=\"robots\" content=\"all\"></head><body></body></html>";
        var context = CreateContext();

        var result = await _step.CleanAsync(html, context);

        Assert.NotNull(context.Metadata);
        Assert.Equal("Test Page", context.Metadata!.Title);
        Assert.Equal("A test", context.Metadata!.Description);
        Assert.Equal("en", context.Metadata!.Language);
        Assert.Equal("all", context.Metadata!.Robots);
    }

    [Fact]
    public async Task Passes_Through_Output_Unchanged()
    {
        var html = "<html><head><meta name=\"robots\" content=\"index\"></head><body><p>Content</p></body></html>";
        var context = CreateContext();

        var result = await _step.CleanAsync(html, context);

        Assert.Equal(html, result.Output);
        Assert.False(result.AiCleaned);
    }
}
