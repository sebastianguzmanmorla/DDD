using Consumer.Contracts.Interfaces.Localization;
using NSubstitute;
using SebastianGuzmanMorla.DDD.Domain.Interfaces;

namespace SebastianGuzmanMorla.DDD.Testing;

public abstract class ValidatorTestBase
{
    protected readonly IServiceProvider ServiceProvider = Substitute.For<IServiceProvider>();
    protected readonly IRuleLocalization RuleLocalization = Substitute.For<IRuleLocalization>();
    protected readonly IGeneralLocalization GeneralLocalization = Substitute.For<IGeneralLocalization>();

    protected ValidatorTestBase()
    {
        ServiceProvider.GetService(typeof(IRuleLocalization)).Returns(RuleLocalization);
        ServiceProvider.GetService(typeof(IGeneralLocalization)).Returns(GeneralLocalization);
        GeneralLocalization.Customer.Returns("Customer");
        RuleLocalization.NotNull(Arg.Any<string>()).Returns(x => $"{x.Arg<string>()} is null");
        RuleLocalization.NotEmpty(Arg.Any<string>()).Returns(x => $"{x.Arg<string>()} is empty");
        RuleLocalization.AlreadyExists(Arg.Any<string>()).Returns(x => $"{x.Arg<string>()} already exists");
        RuleLocalization.Minimum(Arg.Any<string>(), Arg.Any<int>()).Returns(x => $"{x.ArgAt<string>(0)} min {x.ArgAt<int>(1)}");
        RuleLocalization.Maximum(Arg.Any<string>(), Arg.Any<int>()).Returns(x => $"{x.ArgAt<string>(0)} max {x.ArgAt<int>(1)}");
    }
}
