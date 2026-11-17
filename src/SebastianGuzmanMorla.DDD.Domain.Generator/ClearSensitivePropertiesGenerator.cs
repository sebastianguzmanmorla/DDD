using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace SebastianGuzmanMorla.DDD.Domain.Generator;

[Generator]
public sealed class ClearSensitivePropertiesGenerator : IIncrementalGenerator
{
    private const string GeneratorName = "SebastianGuzmanMorla.DDD.Domain.Generator.ClearSensitivePropertiesGenerator";

    private static readonly DiagnosticDescriptor UnclearableSensitiveProperty = new(
        "DDD001",
        "Sensitive property cannot be cleared",
        "Generated cleanup for '{0}' cannot clear sensitive property '{1}'; provide a cleanup override that clears it",
        "Security",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        IncrementalValuesProvider<ClassDeclarationSyntax> classDeclarations =
            context.SyntaxProvider.CreateSyntaxProvider(
                static (node, _) => node is ClassDeclarationSyntax { BaseList: not null },
                static (ctx, _) => (ClassDeclarationSyntax)ctx.Node
            );

        IncrementalValueProvider<ImmutableArray<ClassDeclarationSyntax>> candidates = classDeclarations.Collect();

        context.RegisterSourceOutput(context.CompilationProvider.Combine(candidates),
            static (spc, source) => Execute(source.Left, source.Right, spc));
    }

    private static void Execute(Compilation compilation, ImmutableArray<ClassDeclarationSyntax> classes,
        SourceProductionContext context)
    {
        if (classes.IsDefaultOrEmpty)
        {
            return;
        }

        INamedTypeSymbol? requestSymbol = compilation.GetTypeByMetadataName("SebastianGuzmanMorla.DDD.Domain.Messaging.Request");
        INamedTypeSymbol? sensitiveAttributeSymbol =
            compilation.GetTypeByMetadataName("SebastianGuzmanMorla.DDD.Domain.Attributes.SensitiveDataAttribute");

        if (requestSymbol is null || sensitiveAttributeSymbol is null)
        {
            return;
        }

        HashSet<ISymbol> generated = new(SymbolEqualityComparer.Default);
        foreach (ClassDeclarationSyntax classDeclaration in classes)
        {
            SemanticModel semanticModel = compilation.GetSemanticModel(classDeclaration.SyntaxTree);
            if (semanticModel.GetDeclaredSymbol(classDeclaration) is not { } classSymbol)
            {
                continue;
            }

            if (classSymbol.IsAbstract || !InheritsFrom(classSymbol, requestSymbol))
            {
                continue;
            }

            if (!generated.Add(classSymbol)) continue;

            // Only generate if it is partial
            if (!classDeclaration.Modifiers.Any(m => m.IsKind(SyntaxKind.PartialKeyword)))
            {
                continue;
            }

            // Only generate if it doesn't already have an implementation
            if (classSymbol.GetMembers("ClearSensitiveProperties").Any(m => m is IMethodSymbol
                {
                    IsOverride: true, IsImplicitlyDeclared: false, Arity: 0, Parameters.Length: 0
                }))
            {
                continue;
            }

            var (baseCleanup, baseCleanupVerified) = FindBaseCleanup(classSymbol, requestSymbol);
            List<IPropertySymbol> sensitiveProperties = new();
            HashSet<IPropertySymbol> selectedProperties = new(SymbolEqualityComparer.Default);
            for (INamedTypeSymbol? current = classSymbol; current is not null; current = current.BaseType)
            {
                foreach (IPropertySymbol property in current.GetMembers().OfType<IPropertySymbol>())
                {
                    if (!property.GetAttributes().Any(attribute =>
                            SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, sensitiveAttributeSymbol)))
                    {
                        continue;
                    }

                    IPropertySymbol effectiveProperty = FindEffectiveProperty(property, classSymbol);
                    if (CanAssign(effectiveProperty, classSymbol, compilation))
                    {
                        if (selectedProperties.Add(effectiveProperty)) sensitiveProperties.Add(effectiveProperty);
                    }
                    else if (!baseCleanupVerified || baseCleanup is null ||
                             (!SymbolEqualityComparer.Default.Equals(baseCleanup, effectiveProperty.ContainingType) &&
                              !InheritsFrom(baseCleanup, effectiveProperty.ContainingType)))
                    {
                        Location location = property.Locations.FirstOrDefault(candidate => candidate.IsInSource)
                                            ?? classDeclaration.Identifier.GetLocation();
                        context.ReportDiagnostic(Diagnostic.Create(UnclearableSensitiveProperty, location,
                            classSymbol.ToDisplayString(), property.ToDisplayString()));
                    }
                }
            }

            StringBuilder sb = new();
            sb.AppendLine("// <auto-generated />");
            sb.AppendLine("#nullable enable");
            sb.AppendLine();
            if (!classSymbol.ContainingNamespace.IsGlobalNamespace)
                sb.AppendLine($"namespace {classSymbol.ContainingNamespace.ToDisplayString()};");
            sb.AppendLine();

            // Reopen each containing partial type, preserving accessibility and generic parameters.
            Stack<INamedTypeSymbol> containers = new();
            for (INamedTypeSymbol? current = classSymbol; current is not null; current = current.ContainingType)
                containers.Push(current);
            int typeCount = containers.Count;
            foreach (INamedTypeSymbol type in containers)
            {
                TypeDeclarationSyntax declaration = (TypeDeclarationSyntax)type.DeclaringSyntaxReferences[0].GetSyntax();
                string keyword = declaration is RecordDeclarationSyntax record
                    ? $"record {record.ClassOrStructKeyword.Text}"
                    : declaration.Keyword.Text;
                sb.AppendLine($"{declaration.Modifiers} {keyword} @{type.Name}{declaration.TypeParameterList}");
                sb.AppendLine("{");
            }
            sb.AppendLine($"    [global::System.CodeDom.Compiler.GeneratedCodeAttribute(\"{GeneratorName}\", \"2\")]");
            sb.AppendLine("    public override void ClearSensitiveProperties()");
            sb.AppendLine("    {");

            // Preserve custom redaction in a base class, including its private state.
            if (baseCleanup is not null) sb.AppendLine("        base.ClearSensitiveProperties();");

            foreach (IPropertySymbol prop in sensitiveProperties)
            {
                string receiver = "this";
                if (prop.IsStatic)
                {
                    receiver = prop.ContainingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                }
                else if (!SymbolEqualityComparer.Default.Equals(prop.ContainingType, classSymbol))
                {
                    // A derived property may hide a public sensitive property with the same name.
                    receiver = UsesDeclaringTypeReceiver(prop)
                        ? $"(({prop.ContainingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)})this)"
                        : "base";
                }

                sb.AppendLine($"        {receiver}.@{prop.Name} = default;");
            }

            sb.AppendLine("    }");
            for (int i = 0; i < typeCount; i++) sb.AppendLine("}");

            string identity = classSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            using var hash = System.Security.Cryptography.SHA256.Create();
            string hint = System.BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(identity))).Replace("-", "");
            context.AddSource($"{hint}.ClearSensitiveProperties.g.cs", sb.ToString());
        }
    }

    private static bool CanAssign(IPropertySymbol property, INamedTypeSymbol target, Compilation compilation)
    {
        if (property.SetMethod is not { IsInitOnly: false } setter || property.IsIndexer ||
            !property.ExplicitInterfaceImplementations.IsEmpty ||
            !compilation.IsSymbolAccessibleWithin(property, target) ||
            !compilation.IsSymbolAccessibleWithin(setter, target))
        {
            return false;
        }

        if (property.IsStatic || UsesDeclaringTypeReceiver(property) ||
            SymbolEqualityComparer.Default.Equals(target, property.ContainingType))
        {
            return true;
        }

        // The base-qualified member must not resolve to a different property in the hierarchy.
        for (INamedTypeSymbol? current = target.BaseType; current is not null &&
             !SymbolEqualityComparer.Default.Equals(current, property.ContainingType); current = current.BaseType)
        {
            foreach (ISymbol member in current.GetMembers(property.Name))
            {
                if (compilation.IsSymbolAccessibleWithin(member, target) &&
                    (member is not IPropertySymbol candidate || !Overrides(candidate, property)))
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static bool UsesDeclaringTypeReceiver(IPropertySymbol property) =>
        property.DeclaredAccessibility == Accessibility.Public &&
        property.SetMethod?.DeclaredAccessibility == Accessibility.Public;

    private static IPropertySymbol FindEffectiveProperty(IPropertySymbol property, INamedTypeSymbol target)
    {
        for (INamedTypeSymbol? current = target; current is not null &&
             !SymbolEqualityComparer.Default.Equals(current, property.ContainingType); current = current.BaseType)
        {
            IPropertySymbol? candidate = current.GetMembers(property.Name).OfType<IPropertySymbol>()
                .FirstOrDefault(member => Overrides(member, property));
            if (candidate is not null) return candidate;
        }

        return property;
    }

    private static bool Overrides(IPropertySymbol candidate, IPropertySymbol property)
    {
        for (IPropertySymbol? current = candidate.OverriddenProperty; current is not null; current = current.OverriddenProperty)
        {
            if (SymbolEqualityComparer.Default.Equals(current, property)) return true;
        }

        return false;
    }

    private static (INamedTypeSymbol? Type, bool Verified) FindBaseCleanup(
        INamedTypeSymbol target, INamedTypeSymbol requestSymbol)
    {
        for (INamedTypeSymbol? current = target.BaseType; current is not null; current = current.BaseType)
        {
            IMethodSymbol? method = current.GetMembers("ClearSensitiveProperties").OfType<IMethodSymbol>()
                .FirstOrDefault(candidate => candidate.IsOverride && candidate.Arity == 0 &&
                    candidate.Parameters.Length == 0);
            if (method is not null)
            {
                if (method.IsAbstract) return (null, false);

                // Older compiled generators omitted some inherited properties without reporting them.
                bool verified = method.DeclaringSyntaxReferences.Length != 0 || method.GetAttributes().Any(attribute =>
                    attribute.AttributeClass?.ToDisplayString() == "System.CodeDom.Compiler.GeneratedCodeAttribute" &&
                    attribute.ConstructorArguments.Length == 2 &&
                    attribute.ConstructorArguments[0].Value as string == GeneratorName &&
                    attribute.ConstructorArguments[1].Value as string == "2");
                return (current, verified);
            }

            if (!current.IsAbstract && InheritsFrom(current, requestSymbol) &&
                current.DeclaringSyntaxReferences.Any(reference => reference.GetSyntax() is ClassDeclarationSyntax declaration &&
                    declaration.BaseList is not null && declaration.Modifiers.Any(SyntaxKind.PartialKeyword)))
            {
                return (current, true);
            }
        }

        return (null, false);
    }

    private static bool InheritsFrom(INamedTypeSymbol type, INamedTypeSymbol baseType)
    {
        INamedTypeSymbol? current = type.BaseType;

        while (current is not null)
        {
            if (SymbolEqualityComparer.Default.Equals(current, baseType))
            {
                return true;
            }

            if (SymbolEqualityComparer.Default.Equals(current.OriginalDefinition, baseType))
            {
                return true;
            }

            current = current.BaseType;
        }

        return false;
    }
}
