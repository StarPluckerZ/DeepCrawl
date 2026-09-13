using DeepCrawl.Core.Services;
using DeepCrawl.Domain.Abstractions;
using DeepCrawl.Infrastructure.AI;
using DeepCrawl.Infrastructure.Auth;
using DeepCrawl.Infrastructure.Cleaning;
using DeepSeekSDK;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DeepCrawl.Infrastructure;

public static partial class ServiceCollectionExtensions
{
    /// <summary>
    /// The cleaning pipeline: metadata extraction, tag stripping, qRead main-content
    /// extraction, Markdown conversion, optional AI polish.
    /// </summary>
    private static IServiceCollection AddCleaning(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<IContentAnalyzer, ContentAnalyzer>();
        services.AddSingleton<IHtmlCleaner, AngleSharpHtmlCleaner>();
        services.AddSingleton<IMarkdownConverter, ReverseMarkdownConverter>();
        services.AddSingleton<IAIMarkdownCleaner, OpenAIMarkdownCleaner>();

        // Html stage: metadata (0) → tag removal (10) → qRead extraction (15) → data URI strip (20)
        // Markdown stage: conversion (10) → whitespace normalize (15) → AI polish (20)
        services.AddSingleton<ICleanStep, MetadataExtractorStep>();
        services.AddSingleton<ICleanStep, AngleSharpHtmlCleanerStep>();
        services.AddSingleton<ICleanStep, QReadExtractStep>();
        services.AddSingleton<ICleanStep, StripDataUriStep>();
        services.AddSingleton<ICleanStep, ReverseMarkdownStep>();
        services.AddSingleton<ICleanStep, WhitespaceNormalizeStep>();
        services.AddSingleton<ICleanStep, OpenAICleanStep>();

        services.AddSingleton<CleanPipeline>();
        services.AddSingleton<IRobotsTxtService, RobotsTxtService>();
        services.AddSingleton<ITokenValidator, TokenValidator>();

        // AI
        var endpoint = configuration["AI:BaseUrl"] ?? "https://api.siliconflow.cn/v1/chat/completions";
        var apiKey = configuration["AI:ApiKey"] ?? "";
        var model = configuration["AI:Model"] ?? "Qwen/Qwen3-8B";
        if (string.IsNullOrWhiteSpace(apiKey))
            Console.WriteLine("[WARN] AI:ApiKey is empty — AI cleaning will be skipped.");

        services.AddSingleton(new AIMarkdownCleanerOptions
        {
            BaseUrl = endpoint, ApiKey = apiKey, Model = model,
            ThinkingLevel = configuration["AI:ThinkingLevel"]
        });
        services.AddDeepSeekClient(apiKey, endpoint);

        return services;
    }
}
