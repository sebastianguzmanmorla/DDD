using System.Collections.Immutable;
using System.Reflection;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;

namespace SebastianGuzmanMorla.DDD.Testing;

internal static class GeneratorCompilation
{
    public static void Verify(string source, IIncrementalGenerator generator, Action<Assembly> verify)
    {
        IEnumerable<PortableExecutableReference> references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
            .Select(path => MetadataReference.CreateFromFile(path));
        var compilation = CSharpCompilation.Create("GeneratedTests_" + Guid.NewGuid().ToString("N"),
            [CSharpSyntaxTree.ParseText(source)], references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));
        GeneratorDriver driver = CSharpGeneratorDriver.Create(generator);
        driver.RunGeneratorsAndUpdateCompilation(compilation, out Compilation output, out ImmutableArray<Diagnostic> diagnostics);
        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        using var stream = new MemoryStream();
        EmitResult result = output.Emit(stream);
        Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics));
        stream.Position = 0;
        var context = new AssemblyLoadContext("generator-test", isCollectible: true);
        try { verify(context.LoadFromStream(stream)); }
        finally { context.Unload(); }
    }
}
