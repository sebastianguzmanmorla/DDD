# 2. Smart Enums (`SebastianGuzmanMorla.SmartEnum`)

Use `SmartEnum` instead of native C# enums to associate values, metadata properties, and domain behavior with enum choices.

---

## Single Smart Enum (`SmartEnum<TEnum, TKey>`)

Inherit from `SmartEnum<TEnum, TKey>`. Annotate with `[JsonConverter(typeof(SmartEnumJsonConverter<TEnum, TKey>))]` and `[GenerateSmartEnum]`. Mark the class as `partial`.

```csharp
using System.Text.Json.Serialization;
using SebastianGuzmanMorla.SmartEnum;
using SebastianGuzmanMorla.SmartEnum.Attributes;
using SebastianGuzmanMorla.SmartEnum.Converters.Json;

namespace MyProject.Contracts.Data.Enums;

[JsonConverter(typeof(SmartEnumJsonConverter<Scope, string>))]
[GenerateSmartEnum]
public sealed partial class Scope : SmartEnum<Scope, string>
{
    public static readonly Scope OpenId = new("openid", false);
    public static readonly Scope Profile = new("profile", false);
    public static readonly Scope Customers = new("customers:read", true);

    private Scope(string value, bool requiresSecret) : base(value)
    {
        RequiresSecret = requiresSecret;
    }

    // Type-safe policy name helper for ASP.NET Core authorization
    public string PolicyName => $"Scope:{Value}";

    // Additional domain metadata property attached to each enum choice
    public bool RequiresSecret { get; private set; }
}
```

---

## Flag Smart Enum (`SmartEnumFlags<TFlags, TEnum, TKey>`)

For collections or bitwise flags of a `SmartEnum`, inherit from `SmartEnumFlags<TFlags, TEnum, TKey>`. Annotate with `[JsonConverter(typeof(SmartEnumFlagsJsonConverter<TFlags, TEnum, TKey>))]`.

```csharp
using System.Text.Json.Serialization;
using SebastianGuzmanMorla.SmartEnum;
using SebastianGuzmanMorla.SmartEnum.Converters.Json;

namespace MyProject.Contracts.Data.Enums;

[JsonConverter(typeof(SmartEnumFlagsJsonConverter<Scopes, Scope, string>))]
public sealed class Scopes : SmartEnumFlags<Scopes, Scope, string>
{
    public static readonly Scopes StandardScopes = new(Scope.OpenId, Scope.Profile);

    public Scopes(params Scope[] flags) : base(flags) { }
    public Scopes() { }
}
```
