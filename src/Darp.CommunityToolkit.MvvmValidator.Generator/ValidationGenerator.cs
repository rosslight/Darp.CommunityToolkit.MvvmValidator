using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Darp.MvvmValidator.Generator;

/// <summary>
/// Generates reflection-free validation for every partial type deriving from ValidatingObservableObject.
/// Problems are reported by <see cref="ValidationAnalyzer"/>; this generator only skips what it cannot generate.
/// </summary>
[Generator(LanguageNames.CSharp)]
public sealed class ValidationGenerator : IIncrementalGenerator
{
    /// <inheritdoc />
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        IncrementalValuesProvider<GeneratedFile?> files = context.SyntaxProvider.CreateSyntaxProvider(
            static (node, _) => node is ClassDeclarationSyntax,
            static (syntaxContext, cancellationToken) =>
            {
                if (
                    syntaxContext.SemanticModel.GetDeclaredSymbol(syntaxContext.Node, cancellationToken)
                        is not INamedTypeSymbol type
                    // Visit each type once, even if it has several partial declarations
                    || type.DeclaringSyntaxReferences[0].GetSyntax(cancellationToken) != syntaxContext.Node
                )
                {
                    return null;
                }
                Compilation compilation = syntaxContext.SemanticModel.Compilation;
                var known = KnownSymbols.Create(compilation);
                if (!known.IsValidatingObservableObject(type))
                    return null;
                TypeValidation validation = ValidationModel.Build(type, known, compilation, cancellationToken);
                if (!validation.CanGenerate || validation.Properties.IsEmpty)
                    return null;
                return new GeneratedFile(
                    ValidationSourceWriter.GetHintName(type),
                    ValidationSourceWriter.Write(validation)
                );
            }
        );

        context.RegisterSourceOutput(
            files,
            static (output, file) =>
            {
                if (file is not null)
                    output.AddSource(file.HintName, file.Source);
            }
        );
    }

    private sealed record GeneratedFile(string HintName, string Source);
}
