using Consumer.Contracts.Interfaces.Localization;
using Consumer.Domain.Interfaces.Repositories;
using Microsoft.Extensions.DependencyInjection;
using SebastianGuzmanMorla.DDD.Domain.Interfaces;
using SebastianGuzmanMorla.Validator;

namespace Consumer.Domain.Validators.Customers;

public class CreateCustomerRequestValidator : Contracts.Validators.Customers.CreateCustomerRequestValidator
{
    public CreateCustomerRequestValidator()
    {
        RuleFor(x => x.Name).Must(
            async (sp, request, ct) => !await sp.GetRequiredService<ICustomerRepository>().NameExists(request.Name!, ct),
            (sp, _) => sp.GetRequiredService<IRuleLocalization>().AlreadyExists(
                sp.GetRequiredService<IGeneralLocalization>().Customer),
            ValidationErrorHandle.StopAll);
    }
}
