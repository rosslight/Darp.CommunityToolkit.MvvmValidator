using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Darp.MvvmValidator.Generator;

/// <summary>
/// Reports validation that <see cref="ValidationGenerator"/> cannot generate.
/// Also replaces MVVMTK0006, which this package turns off because it only accepts ObservableValidator.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ValidationAnalyzer : DiagnosticAnalyzer
{
    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
    [
        Diagnostics.TypeMustBePartial,
        Diagnostics.InvalidCustomValidationMethod,
        Diagnostics.MissingValidationBase,
        Diagnostics.InvalidCompareTarget,
        Diagnostics.UnsupportedAttribute,
    ];

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        if (context is null)
            throw new ArgumentNullException(nameof(context));
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(static compilationContext =>
        {
            var known = KnownSymbols.Create(compilationContext.Compilation);
            if (known.ValidatingObservableObject is null)
                return;
            compilationContext.RegisterSymbolAction(
                symbolContext => AnalyzeType(symbolContext, known),
                SymbolKind.NamedType
            );
        });
    }

    private static void AnalyzeType(SymbolAnalysisContext context, KnownSymbols known)
    {
        var type = (INamedTypeSymbol)context.Symbol;
        if (known.IsValidatingObservableObject(type))
        {
            TypeValidation validation = ValidationModel.Build(
                type,
                known,
                context.Compilation,
                context.CancellationToken
            );
            foreach (Diagnostic diagnostic in validation.Diagnostics)
                context.ReportDiagnostic(diagnostic);
            return;
        }
        if (KnownSymbols.InheritsFrom(type, known.ObservableValidator))
            return;

        foreach (ISymbol member in type.GetMembers())
        {
            if (
                member is IFieldSymbol or IPropertySymbol
                && known.IsObservableProperty(member)
                && ValidationModel.HasValidationAttributes(member, known, context.Compilation)
            )
            {
                context.ReportDiagnostic(
                    Diagnostic.Create(Diagnostics.MissingValidationBase, member.Locations[0], member.Name, type.Name)
                );
            }
        }
    }
}
