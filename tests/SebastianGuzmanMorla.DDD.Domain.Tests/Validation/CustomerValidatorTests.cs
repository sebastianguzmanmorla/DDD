using SebastianGuzmanMorla.DDD.Testing;
using Consumer.Contracts.Messaging.Customers;
using Consumer.Domain.Interfaces.Repositories;
using NSubstitute;
using ContractValidator = Consumer.Contracts.Validators.Customers.CreateCustomerRequestValidator;
using DomainValidator = Consumer.Domain.Validators.Customers.CreateCustomerRequestValidator;
using SebastianGuzmanMorla.Validator;

namespace SebastianGuzmanMorla.DDD.Domain.Tests.Validation;

public class CustomerValidatorTests : ValidatorTestBase
{
    private readonly ICustomerRepository _customers = Substitute.For<ICustomerRepository>();

    public CustomerValidatorTests()
    {
        ServiceProvider.GetService(typeof(ICustomerRepository)).Returns(_customers);
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("Ada", true)]
    public async Task ContractValidator_ChecksSyntaxWithoutRepositoryAccess(string? name, bool valid)
    {
        ValidationResult result = await new ContractValidator().Validate(new CreateCustomerRequest { Name = name }, ServiceProvider);
        Assert.Equal(valid, result.IsValid);
        Assert.Empty(_customers.ReceivedCalls());
        ServiceProvider.DidNotReceive().GetService(typeof(ICustomerRepository));
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task DomainValidator_ChecksRepositoryAndForwardsCancellation(bool exists, bool valid)
    {
        using var cts = new CancellationTokenSource();
        _customers.NameExists("Ada", cts.Token).Returns(exists);

        ValidationResult result = await new DomainValidator().Validate(new CreateCustomerRequest { Name = "Ada" }, ServiceProvider, cts.Token);

        Assert.Equal(valid, result.IsValid);
        await _customers.Received(1).NameExists("Ada", cts.Token);
        if (!valid)
        {
            KeyValuePair<string, List<string>> error = Assert.Single(result.Errors!);
            Assert.Equal("$.Name", error.Key);
            Assert.Equal("Customer already exists", Assert.Single(error.Value));
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task DomainValidator_StopsBeforeDatabaseWhenSyntaxIsInvalid(string? name)
    {
        ValidationResult result = await new DomainValidator().Validate(new CreateCustomerRequest { Name = name }, ServiceProvider);
        Assert.False(result.IsValid);
        Assert.Empty(_customers.ReceivedCalls());
    }
}
