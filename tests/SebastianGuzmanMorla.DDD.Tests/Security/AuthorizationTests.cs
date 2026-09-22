using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using SebastianGuzmanMorla.DDD.Middleware;
using SebastianGuzmanMorla.SmartEnum;
using SebastianGuzmanMorla.SmartEnum.Attributes;

namespace SebastianGuzmanMorla.DDD.Tests.Security;

public sealed class AuthorizationTests
{
    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("unknown", false)]
    [InlineData("customers:write", false)]
    [InlineData("customers:read", true)]
    [InlineData("customers:write customers:read", true)]
    public async Task Policy_RequiresExpectedScope(string? value, bool authorized)
    {
        Claim[] claims = value is null ? Array.Empty<Claim>() : [new Claim("scope", value)];
        Assert.Equal(authorized, await Authorize(new ClaimsIdentity(claims, "test")));
    }

    [Fact]
    public async Task Policy_RejectsUnauthenticatedIdentityEvenWithScope() =>
        Assert.False(await Authorize(new ClaimsIdentity([new Claim("scope", "customers:read")])));

    [Fact]
    public async Task Policy_RecognizesScopeInSubsequentClaims() =>
        Assert.True(await Authorize(new ClaimsIdentity([new Claim("scope", "customers:write"), new Claim("scope", "customers:read")], "test")));

    [Fact]
    public async Task Policy_RespectsConfiguredClaimType()
    {
        Assert.True(await Authorize(new ClaimsIdentity([new Claim("permissions", "customers:read")], "test"), "permissions"));
        Assert.False(await Authorize(new ClaimsIdentity([new Claim("scope", "customers:read")], "test"), "permissions"));
    }

    private static async Task<bool> Authorize(ClaimsIdentity identity, string claimType = "scope")
    {
        await using ServiceProvider services = new ServiceCollection().AddLogging()
            .AddSingleton<IAuthorizationHandler>(new SmartEnumRequirementHandler<TestScopes, TestScope, string>(claimType))
            .AddAuthorization(options => options.AddPolicy("Read", policy => policy.RequireAuthenticatedUser()
                .AddRequirements(new SmartEnumRequirement<TestScopes, TestScope, string>(TestScope.Read))))
            .BuildServiceProvider();
        return (await services.GetRequiredService<IAuthorizationService>().AuthorizeAsync(new ClaimsPrincipal(identity), null, "Read")).Succeeded;
    }
}

[GenerateSmartEnum]
public sealed partial class TestScope : SmartEnum<TestScope, string>
{
    public static readonly TestScope Read = new("customers:read");
    public static readonly TestScope Write = new("customers:write");
    private TestScope(string value) : base(value) { }
}

public sealed class TestScopes : SmartEnumFlags<TestScopes, TestScope, string>;
