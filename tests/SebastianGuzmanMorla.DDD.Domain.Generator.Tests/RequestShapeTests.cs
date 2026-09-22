using SebastianGuzmanMorla.DDD.Domain.Messaging;
using SebastianGuzmanMorla.DDD.Testing;

namespace SebastianGuzmanMorla.DDD.Domain.Generator.Tests;

public sealed class RequestShapeTests
{
    [Theory]
    [InlineData("global")]
    [InlineData("internal")]
    [InlineData("generic")]
    [InlineData("nested")]
    [InlineData("duplicate-names")]
    [InlineData("split-partial")]
    [InlineData("generated-base")]
    public void GeneratedRequests_CompileAndRedactSecretsAcrossSupportedTypeShapes(string shape)
    {
        const string header = """
            using SebastianGuzmanMorla.DDD.Domain.Attributes;
            using SebastianGuzmanMorla.DDD.Domain.Messaging;
            """;
        const string property = "[SensitiveData] public string? Secret { get; set; } = \"private\";";
        var source = shape switch
        {
            "global" => $"public partial class Login : Request<Response> {{ {property} }}",
            "internal" => $"namespace A; internal partial class Login : Request<Response> {{ {property} }}",
            "generic" => $"namespace A; public partial class Login<T> : Request<Response> where T : class {{ {property} }}",
            "nested" => $"namespace A; public partial class Container<T> where T : class {{ public partial class Login : Request<Response> {{ {property} }} }}",
            "duplicate-names" => $"namespace A {{ public partial class Login : Request<Response> {{ {property} }} }} namespace B {{ public partial class Login : Request<Response> {{ {property} }} }}",
            "split-partial" => $"namespace A; public partial class Login : Request<Response> {{ {property} }} public partial class Login : Request<Response> {{ }}",
            "generated-base" => """
                namespace A;
                public partial class Credentials : Request<Response>
                {
                    [SensitiveData] private string? Secret { get; set; } = "private";
                    public string? ReadSecret() => Secret;
                }
                public partial class Login : Credentials { }
                """,
            _ => throw new ArgumentOutOfRangeException(nameof(shape))
        };
        GeneratorCompilation.Verify(header + "\n" + source, new ClearSensitivePropertiesGenerator(), assembly =>
        {
            foreach (Type? definition in assembly.GetTypes().Where(t => t.Name.StartsWith("Login", StringComparison.Ordinal)))
            {
                Type type = definition.ContainsGenericParameters ? definition.MakeGenericType(typeof(string)) : definition;
                var instance = (Request)Activator.CreateInstance(type)!;
                object? ReadSecret() => shape == "generated-base"
                    ? type.GetMethod("ReadSecret")!.Invoke(instance, null)
                    : type.GetProperty("Secret")!.GetValue(instance);
                Assert.Equal("private", ReadSecret());
                instance.ClearSensitiveProperties();
                Assert.Null(ReadSecret());
            }
        });
    }
}
