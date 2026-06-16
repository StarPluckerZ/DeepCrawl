using DeepCrawl.Core.Dtos;
using FluentValidation;

namespace DeepCrawl.Core.Validators;

public class ScrapeRequestValidator : AbstractValidator<ScrapeRequest>
{
    public ScrapeRequestValidator()
    {
        RuleFor(x => x.Url)
            .NotEmpty().WithMessage("URL is required")
            .Must(url => UrlGuard.Validate(url).Ok)
            .WithMessage(x => UrlGuard.Validate(x.Url).Error ?? "Invalid URL");
    }
}
