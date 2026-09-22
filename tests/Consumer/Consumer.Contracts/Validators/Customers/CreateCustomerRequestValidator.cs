using Microsoft.Extensions.DependencyInjection;
using SebastianGuzmanMorla.DDD.Domain.Interfaces;
using SebastianGuzmanMorla.Validator;
using Consumer.Contracts.Messaging.Customers;

namespace Consumer.Contracts.Validators.Customers;

public class CreateCustomerRequestValidator : Validator<CreateCustomerRequest>
{
    public CreateCustomerRequestValidator()
    {
        RuleFor(x => x.Name)
            .NotNull((sp, _) => sp.GetRequiredService<IRuleLocalization>().NotNull(nameof(CreateCustomerRequest.Name)), ValidationErrorHandle.StopAll)
            .NotEmpty((sp, _) => sp.GetRequiredService<IRuleLocalization>().NotEmpty(nameof(CreateCustomerRequest.Name)), ValidationErrorHandle.StopAll);
    }
}
