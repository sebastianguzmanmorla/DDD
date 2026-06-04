using Microsoft.Extensions.DependencyInjection;
using SebastianGuzmanMorla.DDD.Domain.Interfaces;
using SebastianGuzmanMorla.DDD.Interfaces;
using SebastianGuzmanMorla.Validator;

namespace SebastianGuzmanMorla.DDD.Validators;

public class PageValidator : Validator<IPageValidation>
{
    public PageValidator()
    {
        RuleForWhen(x => x.Page, (_, request, _) => Task.FromResult(request.Page != null))
            .Minimum(1,
                (serviceProvider, _) => serviceProvider.GetRequiredService<IRuleLocalization>()
                    .Minimum(nameof(IPageValidation.Page), 1));

        RuleForWhen(x => x.Size, (_, request, _) => Task.FromResult(request.Size != null))
            .Minimum(1,
                (serviceProvider, _) => serviceProvider.GetRequiredService<IRuleLocalization>()
                    .Minimum(nameof(IPageValidation.Size), 1))
            .Maximum(100,
                (serviceProvider, _) => serviceProvider.GetRequiredService<IRuleLocalization>()
                    .Maximum(nameof(IPageValidation.Size), 100));
    }
}
