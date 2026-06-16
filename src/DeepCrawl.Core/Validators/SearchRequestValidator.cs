using DeepCrawl.Core.Dtos;
using FluentValidation;

namespace DeepCrawl.Core.Validators;

public class SearchRequestValidator : AbstractValidator<SearchRequest>
{
    public SearchRequestValidator()
    {
        RuleFor(x => x.Query)
            .NotEmpty().WithMessage("Query is required")
            .MaximumLength(500);

        RuleFor(x => x.Limit).InclusiveBetween(1, 100);
        RuleFor(x => x.Timeout).InclusiveBetween(1000, 300000).Unless(x => x.Timeout is null);
    }
}
