using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Darp.MvvmValidator.Generator;

/// <summary>
/// The trim analyzer reports IL2026 where an attribute with a <c>[RequiresUnreferencedCode]</c> constructor is applied,
/// because reflection could create it. ValidatingObservableObject never reads attributes with reflection, and the
/// generated code avoids the unsafe paths of [Compare] and the length attributes, so these warnings do not apply.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class TrimWarningSuppressor : DiagnosticSuppressor
{
    private static readonly SuppressionDescriptor AttributeUsage = new(
        "DMVS0001",
        "IL2026",
        "The generated validation of ValidatingObservableObject does not use the reflection this attribute constructor warns about"
    );

    /// <inheritdoc />
    public override ImmutableArray<SuppressionDescriptor> SupportedSuppressions { get; } = [AttributeUsage];

    /// <inheritdoc />
    public override void ReportSuppressions(SuppressionAnalysisContext context)
    {
        KnownSymbols? known = null;
        foreach (Diagnostic diagnostic in context.ReportedDiagnostics)
        {
            if (diagnostic.Location.SourceTree is not { } tree)
                continue;
            SyntaxNode node = tree.GetRoot(context.CancellationToken)
                .FindNode(diagnostic.Location.SourceSpan, getInnermostNodeForTie: true);
            if (
                node.FirstAncestorOrSelf<AttributeSyntax>()
                is not { Parent: AttributeListSyntax { Parent: MemberDeclarationSyntax declaration } } attribute
            )
            {
                continue;
            }

            SemanticModel model = context.GetSemanticModel(tree);
            known ??= KnownSymbols.Create(model.Compilation);
            if (
                TryGetMember(declaration, model, known, out INamedTypeSymbol? type, out ITypeSymbol? propertyType)
                && known.IsValidatingObservableObject(type)
                && AttributeRenderer.GetAttributeClass(attribute, model) is { } attributeClass
                && known.IsTrimSafeFor(attributeClass, propertyType)
            )
            {
                context.ReportSuppression(Suppression.Create(AttributeUsage, diagnostic));
            }
        }
    }

    private static bool TryGetMember(
        MemberDeclarationSyntax declaration,
        SemanticModel model,
        KnownSymbols known,
        [NotNullWhen(true)] out INamedTypeSymbol? containingType,
        [NotNullWhen(true)] out ITypeSymbol? propertyType
    )
    {
        switch (declaration)
        {
            case PropertyDeclarationSyntax syntax when model.GetDeclaredSymbol(syntax) is IPropertySymbol property:
                containingType = property.ContainingType;
                propertyType = property.Type;
                return true;
            case FieldDeclarationSyntax { Declaration.Variables: [var variable] }
                when model.GetDeclaredSymbol(variable) is IFieldSymbol field && known.IsObservableProperty(field):
                containingType = field.ContainingType;
                propertyType = field.Type;
                return true;
            default:
                containingType = null;
                propertyType = null;
                return false;
        }
    }
}
