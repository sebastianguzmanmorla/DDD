using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace SebastianGuzmanMorla.DDD.Generator;

[Generator]
public sealed class ConfigureHandlerServicesGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        IncrementalValuesProvider<ClassDeclarationSyntax> classDeclarations =
            context.SyntaxProvider.CreateSyntaxProvider(
                static (node, _) => node is ClassDeclarationSyntax,
                static (ctx, _) => (ClassDeclarationSyntax)ctx.Node
            );

        IncrementalValueProvider<ImmutableArray<ClassDeclarationSyntax>> configureServicesClass = classDeclarations
            .Where(static c =>
                c.Identifier.Text == "ConfigureHandlerServices" &&
                c.Modifiers.Any(m => m.IsKind(SyntaxKind.PartialKeyword)))
            .Collect();

        IncrementalValueProvider<ImmutableArray<ClassDeclarationSyntax>> candidates = classDeclarations
            .Where(static c => c.BaseList is not null)
            .Collect();

        context.RegisterSourceOutput(
            context.CompilationProvider.Combine(candidates).Combine(configureServicesClass),
            static (context, tuple) =>
            {
                ((Compilation? compilation, ImmutableArray<ClassDeclarationSyntax> candidates),
                    ImmutableArray<ClassDeclarationSyntax> configureClasses) = tuple;

                if (configureClasses.Length == 0)
                {
                    return;
                }

                ClassDeclarationSyntax marker = configureClasses.First();
                INamedTypeSymbol? markerSymbol = compilation.GetSemanticModel(marker.SyntaxTree).GetDeclaredSymbol(marker);
                if (markerSymbol is null) return;
                string? targetNamespace = markerSymbol.ContainingNamespace.IsGlobalNamespace
                    ? null : markerSymbol.ContainingNamespace.ToDisplayString();

                INamedTypeSymbol requestHandlerSymbol =
                    compilation.GetTypeByMetadataName("SebastianGuzmanMorla.DDD.Domain.Interfaces.IRequestHandler`2") ??
                    throw new Exception("SebastianGuzmanMorla.DDD.Domain.Interfaces.IRequestHandler`2");

                INamedTypeSymbol notificationHandlerSymbol =
                    compilation.GetTypeByMetadataName("SebastianGuzmanMorla.DDD.Domain.Interfaces.INotificationHandler`1") ??
                    throw new Exception("SebastianGuzmanMorla.DDD.Domain.Interfaces.INotificationHandler`1");

                INamedTypeSymbol requestBinderSymbol =
                    compilation.GetTypeByMetadataName("SebastianGuzmanMorla.DDD.Interfaces.IRequestBinder`2") ??
                    throw new Exception("SebastianGuzmanMorla.DDD.Interfaces.IRequestBinder`2");

                INamedTypeSymbol? pageValidatorSymbol =
                    compilation.GetTypeByMetadataName("SebastianGuzmanMorla.DDD.Validators.PageValidator");


                StringBuilder sourceBuilder = new();

                sourceBuilder.AppendLine("using Microsoft.Extensions.DependencyInjection;");
                sourceBuilder.AppendLine();
                if (targetNamespace is not null) sourceBuilder.AppendLine($"namespace {targetNamespace};");
                sourceBuilder.AppendLine();
                sourceBuilder.AppendLine("public static partial class ConfigureHandlerServices");
                sourceBuilder.AppendLine("{");
                sourceBuilder.AppendLine(
                    "    private static partial void ConfigureGenerated(IServiceCollection services)");
                sourceBuilder.AppendLine("    {");

                if (pageValidatorSymbol is not null)
                {
                    sourceBuilder.AppendLine("        services.AddSingleton<SebastianGuzmanMorla.Validator.Interfaces.IValidator<SebastianGuzmanMorla.DDD.Domain.Interfaces.IPageValidation>, SebastianGuzmanMorla.DDD.Validators.PageValidator>();");
                }

                HashSet<ISymbol> registered = new(SymbolEqualityComparer.Default);
                foreach (ClassDeclarationSyntax? declaration in candidates)
                {
                    SemanticModel semanticModel = compilation.GetSemanticModel(declaration.SyntaxTree);

                    if (ModelExtensions.GetDeclaredSymbol(semanticModel, declaration) is not INamedTypeSymbol
                        namedTypeSymbol)
                    {
                        continue;
                    }

                    if (namedTypeSymbol.IsAbstract || !registered.Add(namedTypeSymbol))
                    {
                        continue;
                    }

                    foreach (INamedTypeSymbol typeSymbol in namedTypeSymbol.AllInterfaces)
                    {
                        if (SymbolEqualityComparer.Default.Equals(typeSymbol.OriginalDefinition, requestHandlerSymbol))
                        {
                            sourceBuilder.AppendLine(
                                $"        services.AddScoped(typeof({typeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}), typeof({namedTypeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}));");
                            continue;
                        }

                        if (SymbolEqualityComparer.Default.Equals(typeSymbol.OriginalDefinition, requestBinderSymbol))
                        {
                            sourceBuilder.AppendLine(
                                $"        services.AddScoped(typeof({typeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}), typeof({namedTypeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}));");
                            continue;
                        }

                        if (SymbolEqualityComparer.Default.Equals(typeSymbol.OriginalDefinition, notificationHandlerSymbol))
                        {
                            sourceBuilder.AppendLine(
                                namedTypeSymbol.IsGenericType
                                    ? $"        services.AddSingleton(typeof({typeSymbol.ConstructUnboundGenericType().ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}), typeof({namedTypeSymbol.ConstructUnboundGenericType().ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}));"
                                    : $"        services.AddSingleton(typeof({typeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}), typeof({namedTypeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}));");
                        }
                    }
                }

                sourceBuilder.AppendLine("    }");
                sourceBuilder.AppendLine("}");

                context.AddSource("ConfigureHandlerServices.g.cs", sourceBuilder.ToString());
            });
    }

}
