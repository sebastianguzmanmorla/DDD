using SebastianGuzmanMorla.DDD.Testing;
using SebastianGuzmanMorla.DDD.Domain.Interfaces;
using SebastianGuzmanMorla.DDD.Validators;
using SebastianGuzmanMorla.Validator;

namespace SebastianGuzmanMorla.DDD.Tests.Validation;

public class PageValidatorTests : ValidatorTestBase
{
    private sealed class PageInput : IPageValidation
    {
        public int? Page { get; init; }
        public int? Size { get; init; }
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData(1, 1)]
    [InlineData(1, 100)]
    [InlineData(20, null)]
    [InlineData(null, 10)]
    public async Task Validate_AllowsDefaultsAndInclusiveBounds(int? page, int? size)
    {
        ValidationResult result = await new PageValidator().Validate(new PageInput { Page = page, Size = size }, ServiceProvider);
        Assert.True(result.IsValid);
        Assert.Null(result.Errors);
    }

    [Theory]
    [InlineData(0, 10, "Page", "Page min 1")]
    [InlineData(-1, 10, "Page", "Page min 1")]
    [InlineData(1, 0, "Size", "Size min 1")]
    [InlineData(1, 101, "Size", "Size max 100")]
    public async Task Validate_InvalidBoundsReturnLocalizedPropertyErrors(int page, int size, string property, string message)
    {
        ValidationResult result = await new PageValidator().Validate(new PageInput { Page = page, Size = size }, ServiceProvider);
        Assert.False(result.IsValid);
        KeyValuePair<string, List<string>> error = Assert.Single(result.Errors!);
        Assert.Equal("$." + property, error.Key);
        Assert.Equal(message, Assert.Single(error.Value));
    }
}
