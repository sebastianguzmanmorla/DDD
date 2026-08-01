# 3. Validation (`SebastianGuzmanMorla.Validator`)

Validation is divided into two distinct architectural phases to enforce boundary separation:

---

## A. Contracts Layer Validators (`[Project].Contracts/Validators`)

Handles syntactic and basic data structure validation (e.g., checks for nulls, empty strings, Guid formatting, regex matches, email format, minimum/maximum lengths).

* **CRITICAL RULE**: Contract validators must **NEVER** access repositories, DbContexts, or external services. They inherit from `Validator<T>`.

```csharp
using SebastianGuzmanMorla.Validator;
using SebastianGuzmanMorla.DDD.Domain.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace MyProject.Contracts.Validators;

public class SampleValidator : Validator<ISampleValidation>
{
    public SampleValidator()
    {
        RuleFor(x => x.Email)
            .NotNull((x, _) => x.GetRequiredService<IRuleLocalization>().NotNull(nameof(ISampleValidation.Email)), ValidationErrorHandle.StopProperty)
            .NotEmpty((x, _) => x.GetRequiredService<IRuleLocalization>().NotEmpty(nameof(ISampleValidation.Email)), ValidationErrorHandle.StopProperty);
    }
}
```

---

## B. Domain Layer Validators (`[Project].Domain/Validators`)

Handles semantic and business rule validation (e.g., database uniqueness checks, foreign key existence, status verification).

* **CRITICAL RULE**: Inherits from the contract validator and utilizes `Must`, `RuleForWhen`, or `ValidateEntity` with dependency-injected repositories.

```csharp
using MyProject.Contracts.Interfaces;
using MyProject.Domain.Interfaces.Repositories;
using Microsoft.Extensions.DependencyInjection;
using SebastianGuzmanMorla.Validator;

namespace MyProject.Domain.Validators;

public class SampleValidator : Contracts.Validators.SampleValidator
{
    public SampleValidator()
    {
        RuleFor(x => x.Email)
            .Must(EmailDoesNotExist, (x, _) =>
            {
                string label = x.GetRequiredService<IGeneralLocalization>().Email;
                return x.GetRequiredService<IRuleLocalization>().AlreadyExists(label);
            }, ValidationErrorHandle.StopAll);
    }

    private static Task<bool> EmailDoesNotExist(IServiceProvider provider, ISampleValidation entity, CancellationToken cancellationToken = default)
    {
        // Must return true if valid (i.e. email does not exist)
        return provider.GetRequiredService<ISampleRepository>().None(entity.Email, cancellationToken);
    }
}
```

---

## Built-in Validation Rules & Flow Control

### Built-in Rules
The custom validator package exposes several chainable extension methods on `RuleFor`:

| Rule | Supported Types | Description |
|---|---|---|
| `NotEmpty` | `string`, `Guid`, `Guid?`, arrays/collections | Validates that string/array is not empty, or Guid is not empty (`Guid.Empty`). |
| `NotNull` | Any object reference | Validates that the property is not `null`. |
| `MinimumLength(min)` | `string` | Asserts string length is at least `min`. |
| `MaximumLength(max)` | `string` | Asserts string length is at most `max`. |
| `EmailAddress` | `string` | Asserts the value is a valid email format. |
| `Equal(value/expression)`| Comparable types | Asserts equality against a constant or another property expression. |
| `NotEqual(value/expression)`| Comparable types | Asserts inequality against a constant or another property expression. |
| `Minimum(min)` | Comparable types | Asserts property is greater than or equal to `min`. |
| `Maximum(max)` | Comparable types | Asserts property is less than or equal to `max`. |
| `Between(min, max)` | Comparable types | Asserts property falls within the `[min, max]` range. |
| `Matches(regex)` | `string` | Asserts property matches the specified regular expression. |
| `Must(expression)` | Any type | Runs a custom asynchronous validation delegate returning a boolean. |

### Validation Flow Control (`ValidationErrorHandle`)
Every validation rule accepts an optional `ValidationErrorHandle` parameter:
* **`ValidationErrorHandle.Continue` (Default)**: If this rule fails, record error and continue validating.
* **`ValidationErrorHandle.StopProperty`**: If this rule fails, abort executing remaining rules for this property.
* **`ValidationErrorHandle.StopAll`**: If this rule fails, halt the entire validation process immediately.

---

## Advanced Validation Features

### Conditional Validation (`RuleForWhen`)
```csharp
RuleForWhen(u => u.PromoCode, (provider, user, ct) => Task.FromResult(user.HasOptedIn))
    .NotEmpty((x, _) => "Promo code is required if opted in");
```

### Nested Entities & Collections Validation (`ValidateEntity`)
```csharp
ValidateEntity(c => c.BillingAddress);
ValidateEntity(c => c.LineItems); // Works for collections as well
```

### Reusable Interface Validation Pattern (Interface Cascading)
To eliminate duplicate validation rules across different requests/entities sharing common fields (like `ClientId` or `UserId`):

1. **Define Interface (`[Project].Contracts/Interfaces`)**:
   ```csharp
   using SebastianGuzmanMorla.Validator.Interfaces;
   
   namespace MyProject.Contracts.Interfaces;
   
   public interface IClientIdValidation : IEntityValidation
   {
       public Guid ClientId { get; set; }
   }
   ```
2. **Implement rules on Interface Validator**: `public class ClientIdValidator : Validator<IClientIdValidation> { ... }`
3. **Implement Interface on target models**: `public class CreateClientRequest : Request<CreateClientResponse>, IClientIdValidation`
4. **Mark concrete validators as partial**:
   ```csharp
   public partial class CreateClientRequestValidator : Validator<CreateClientRequest>
   {
       // Roslyn Source Generator automatically chains execution of IValidator<IClientIdValidation>.Validate(...)
   }
   ```
