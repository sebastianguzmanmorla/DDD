using System.Collections.Immutable;
using System.Reflection;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
using SebastianGuzmanMorla.DDD.Domain.Generator;
using SebastianGuzmanMorla.DDD.Domain.Messaging;

namespace SebastianGuzmanMorla.DDD.Domain.Generator.Tests;

public class SensitiveDataGeneratorTests
{
    [Theory]
    [InlineData("declared")]
    [InlineData("inherited")]
    [InlineData("base-implementation")]
    [InlineData("base-inaccessible-implementation")]
    [InlineData("hidden")]
    [InlineData("protected")]
    [InlineData("protected-override")]
    [InlineData("static")]
    public void ClearSensitiveProperties_RemovesSecretsAndPreservesPublicData(string scenario)
    {
        const string header = """
            using SebastianGuzmanMorla.DDD.Domain.Attributes;
            using SebastianGuzmanMorla.DDD.Domain.Messaging;
            namespace Sample;
            """;
        string source = scenario switch
        {
            "declared" => """
                public partial class Login : Request<Response>
                {
                    [SensitiveData] public string? Password { get; set; } = "secret";
                    public string Name { get; set; } = "visible";
                    public string? ReadSecret() => Password;
                }
                """,
            "inherited" => """
                public abstract class Credentials : Request<Response>
                {
                    [SensitiveData] public string? Password { get; set; } = "secret";
                    public string? ReadSecret() => Password;
                }
                public partial class Login : Credentials
                {
                    public string Name { get; set; } = "visible";
                }
                """,
            "base-implementation" => """
                public abstract class Credentials : Request<Response>
                {
                    private string? _password = "secret";
                    public string? ReadSecret() => _password;
                    public override void ClearSensitiveProperties() => _password = null;
                }
                public partial class Login : Credentials
                {
                    public string Name { get; set; } = "visible";
                }
                """,
            "base-inaccessible-implementation" => """
                public abstract class Credentials : Request<Response>
                {
                    [SensitiveData] public string? Password { get; private set; } = "secret";
                    public string? ReadSecret() => Password;
                    public override void ClearSensitiveProperties() => Password = null;
                }
                public partial class Login : Credentials
                {
                    public string Name { get; set; } = "visible";
                }
                """,
            "hidden" => """
                public abstract class Credentials : Request<Response>
                {
                    [SensitiveData] public string? Name { get; set; } = "secret";
                    public string? ReadSecret() => Name;
                }
                public partial class Login : Credentials
                {
                    public new string Name { get; set; } = "visible";
                }
                """,
            "protected" => """
                public abstract class Credentials : Request<Response>
                {
                    [SensitiveData] protected string? Password { get; set; } = "secret";
                    public string? ReadSecret() => Password;
                }
                public partial class Login : Credentials
                {
                    public string Name { get; set; } = "visible";
                }
                """,
            "protected-override" => """
                public abstract class Credentials : Request<Response>
                {
                    [SensitiveData] public virtual string? Password { get; protected set; } = "base fixture";
                    public string? ReadSecret() => Password;
                }
                public abstract class Intermediate : Credentials
                {
                    public override string? Password { get; protected set; } = "secret";
                }
                public partial class Login : Intermediate
                {
                    public string Name { get; set; } = "visible";
                }
                """,
            "static" => """
                public partial class Login : Request<Response>
                {
                    [SensitiveData] public static string? Password { get; set; } = "secret";
                    public string Name { get; set; } = "visible";
                    public string? ReadSecret() => Password;
                }
                """,
            _ => throw new ArgumentOutOfRangeException(nameof(scenario))
        };
        CSharpCompilation compilation = Compile(header + "\n" + source);
        GeneratorDriver driver = CSharpGeneratorDriver.Create(new ClearSensitivePropertiesGenerator());
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out Compilation output, out ImmutableArray<Diagnostic> diagnostics);
        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.Single(driver.GetRunResult().GeneratedTrees);
        using var stream = new MemoryStream();
        EmitResult result = output.Emit(stream);
        Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics));
        stream.Position = 0;
        var loadContext = new AssemblyLoadContext("generator-test", isCollectible: true);
        try
        {
            Assembly assembly = loadContext.LoadFromStream(stream);
            Type type = assembly.GetType("Sample.Login", throwOnError: true)!;
            var request = (Request)Activator.CreateInstance(type)!;
            Assert.Equal("secret", type.GetMethod("ReadSecret")!.Invoke(request, null));

            request.ClearSensitiveProperties();

            Assert.Null(type.GetMethod("ReadSecret")!.Invoke(request, null));
            Assert.Equal("visible", type.GetProperty("Name", BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)!.GetValue(request));
        }
        finally
        {
            loadContext.Unload();
        }
    }

    [Theory]
    [InlineData("private set;")]
    [InlineData("init;")]
    [InlineData("")]
    public void UnclearableInheritedPropertyReportsAnError(string setter)
    {
        CSharpCompilation compilation = Compile($$"""
            using SebastianGuzmanMorla.DDD.Domain.Attributes;
            using SebastianGuzmanMorla.DDD.Domain.Messaging;

            public abstract class Credentials : Request<Response>
            {
                [SensitiveData] public string? Password { get; {{setter}} }
            }

            public partial class Login : Credentials;
            """);
        GeneratorDriver driver = CSharpGeneratorDriver.Create(new ClearSensitivePropertiesGenerator());

        driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out ImmutableArray<Diagnostic> diagnostics);

        Diagnostic diagnostic = Assert.Single(diagnostics, candidate => candidate.Id == "DDD001");
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Contains("Credentials.Password", diagnostic.GetMessage());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReferencedBaseCleanupIsAcceptedOnlyWhenVerified(bool generated)
    {
        const string source = """
            using SebastianGuzmanMorla.DDD.Domain.Attributes;
            using SebastianGuzmanMorla.DDD.Domain.Messaging;

            public partial class Credentials : Request<Response>
            {
                [SensitiveData] public string? Password { get; private set; }
            }
            """;
        CSharpCompilation baseCompilation = Compile(generated ? source : source + """

            public partial class Credentials
            {
                public override void ClearSensitiveProperties() { }
            }
            """);
        GeneratorDriver driver = CSharpGeneratorDriver.Create(new ClearSensitivePropertiesGenerator());
        Compilation baseOutput = baseCompilation;
        if (generated) driver.RunGeneratorsAndUpdateCompilation(baseCompilation, out baseOutput, out _);

        using var stream = new MemoryStream();
        EmitResult emit = baseOutput.Emit(stream);
        Assert.True(emit.Success, string.Join(Environment.NewLine, emit.Diagnostics));
        CSharpCompilation descendant = Compile("public partial class Login : Credentials;")
            .AddReferences(MetadataReference.CreateFromImage(stream.ToArray()));

        driver.RunGeneratorsAndUpdateCompilation(descendant, out _, out ImmutableArray<Diagnostic> diagnostics);

        Assert.Equal(generated ? 0 : 1, diagnostics.Count(diagnostic => diagnostic.Id == "DDD001"));
    }

    private static CSharpCompilation Compile(string source) => CSharpCompilation.Create(
        "GeneratedTests_" + Guid.NewGuid().ToString("N"),
        [CSharpSyntaxTree.ParseText(source)],
        ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
            .Select(path => MetadataReference.CreateFromFile(path)),
        new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));
}
