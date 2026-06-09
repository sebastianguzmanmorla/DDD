using Microsoft.AspNetCore.Authorization;
using SebastianGuzmanMorla.SmartEnum;

namespace SebastianGuzmanMorla.DDD.Middleware;

public sealed class SmartEnumRequirement<TSmartEnumFlags, TSmartEnum, TValue>(TSmartEnum value) : IAuthorizationRequirement
    where TSmartEnumFlags : SmartEnumFlags<TSmartEnumFlags, TSmartEnum, TValue>, new()
    where TSmartEnum : SmartEnum<TSmartEnum, TValue>
    where TValue : IEquatable<TValue>
{
    public TSmartEnum Value { get; } = value;
}

public sealed class SmartEnumRequirementHandler<TSmartEnumFlags, TSmartEnum, TValue>(string claimType = "scope")
    : AuthorizationHandler<SmartEnumRequirement<TSmartEnumFlags, TSmartEnum, TValue>>
    where TSmartEnumFlags : SmartEnumFlags<TSmartEnumFlags, TSmartEnum, TValue>, new()
    where TSmartEnum : SmartEnum<TSmartEnum, TValue>
    where TValue : IEquatable<TValue>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, SmartEnumRequirement<TSmartEnumFlags, TSmartEnum, TValue> requirement)
    {
        string? claimValue = context.User.FindFirst(claimType)?.Value;

        TSmartEnumFlags flags = SmartEnumFlags<TSmartEnumFlags, TSmartEnum, TValue>.Parse(claimValue);

        if (flags.Has(requirement.Value))
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}
