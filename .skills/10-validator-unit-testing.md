# 10. Validator Unit Testing Pattern

Tests are written using xUnit and `NSubstitute` to mock external services (such as repositories and localizations) resolved from `IServiceProvider`.

---

## A. ValidatorTestBase Template

Utilize a base `ValidatorTestBase` class to pre-configure `IServiceProvider` mocks and stub `IRuleLocalization` and `IGeneralLocalization` methods to prevent `NullReferenceException` during test execution:

```csharp
using NSubstitute;
using SebastianGuzmanMorla.DDD.Domain.Interfaces;
using MyProject.Contracts.Interfaces.Localization;

namespace MyProject.Tests;

public abstract class ValidatorTestBase
{
    protected readonly IGeneralLocalization GeneralLocalization;
    protected readonly IRuleLocalization RuleLocalization;
    protected readonly IServiceProvider ServiceProvider;

    protected ValidatorTestBase()
    {
        ServiceProvider = Substitute.For<IServiceProvider>();
        GeneralLocalization = Substitute.For<IGeneralLocalization>();
        RuleLocalization = Substitute.For<IRuleLocalization>();

        ServiceProvider.GetService(typeof(IGeneralLocalization)).Returns(GeneralLocalization);
        ServiceProvider.GetService(typeof(IRuleLocalization)).Returns(RuleLocalization);

        // Stub RuleLocalization methods to avoid null returns during validation assertion
        RuleLocalization.NotEmpty(Arg.Any<string>()).Returns(x => $"{x.Arg<string>()} is empty");
        RuleLocalization.NotNull(Arg.Any<string>()).Returns(x => $"{x.Arg<string>()} is null");
        RuleLocalization.MaximumLength(Arg.Any<string>(), Arg.Any<int>())
            .Returns(x => $"{x.Arg<string>()} max length is {x.ArgAt<int>(1)}");
        RuleLocalization.NotExists(Arg.Any<string>()).Returns(x => $"{x.Arg<string>()} does not exist");
        RuleLocalization.AlreadyExists(Arg.Any<string>()).Returns(x => $"{x.Arg<string>()} already exists");
        RuleLocalization.NotValid(Arg.Any<string>()).Returns(x => $"{x.Arg<string>()} is not valid");

        // Stub GeneralLocalization property labels
        GeneralLocalization.User.Returns("User");
        GeneralLocalization.Email.Returns("Email");
    }
}
```

---

## B. Private Test Classes & Test Cases

Declare a small private mock class implementing your target validation interface to validate rules in isolation:

```csharp
using MyProject.Contracts.Interfaces;
using MyProject.Domain.Interfaces.Repositories;
using MyProject.Domain.Validators;
using NSubstitute;
using Xunit;
using ValidationResult = SebastianGuzmanMorla.Validator.ValidationResult;

namespace MyProject.Domain.Tests.Validators;

public class DomainValidatorsTests : ValidatorTestBase
{
    private readonly ClientIdValidator _clientIdValidator;
    private readonly IClientRepository _clientRepository;

    public DomainValidatorsTests()
    {
        _clientIdValidator = new ClientIdValidator();
        _clientRepository = Substitute.For<IClientRepository>();

        ServiceProvider.GetService(typeof(IClientRepository)).Returns(_clientRepository);
    }

    [Fact]
    public async Task ClientIdValidator_WhenClientExists_ShouldBeValid()
    {
        Guid clientId = Guid.NewGuid();
        var entity = new TestClientIdValidation { ClientId = clientId };
        _clientRepository.Any(clientId, Arg.Any<CancellationToken>()).Returns(Task.FromResult(true));

        ValidationResult result = await _clientIdValidator.Validate(entity, ServiceProvider);

        Assert.True(result.IsValid);
    }

    private class TestClientIdValidation : IClientIdValidation
    {
        public Guid ClientId { get; set; }
    }
}
```
