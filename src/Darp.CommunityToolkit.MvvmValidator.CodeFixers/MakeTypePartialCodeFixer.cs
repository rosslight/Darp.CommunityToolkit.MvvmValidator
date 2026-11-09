using System.Collections.Immutable;
using System.Composition;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Editing;

namespace Darp.MvvmValidator.CodeFixers;

/// <summary> Fixes DMV0001 by making a validated type and all its containing types partial </summary>
[ExportCodeFixProvider(LanguageNames.CSharp)]
[Shared]
public sealed class MakeTypePartialCodeFixer : CodeFixProvider
{
    private const string Title = "Make type partial";

    /// <inheritdoc />
    public override ImmutableArray<string> FixableDiagnosticIds { get; } = ["DMV0001"];

    /// <inheritdoc />
    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    /// <inheritdoc />
    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        SyntaxNode? root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root?.FindNode(context.Span).FirstAncestorOrSelf<TypeDeclarationSyntax>() is not { } declaration)
            return;
        // A type with several declarations is partial already, so only this declaration and its containing types matter
        List<TypeDeclarationSyntax> nonPartial = declaration
            .AncestorsAndSelf()
            .OfType<TypeDeclarationSyntax>()
            .Where(d => !d.Modifiers.Any(SyntaxKind.PartialKeyword))
            .ToList();
        if (nonPartial.Count == 0)
            return;

        context.RegisterCodeFix(
            CodeAction.Create(
                Title,
                _ => Task.FromResult(MakePartial(context.Document, root, nonPartial)),
                equivalenceKey: Title
            ),
            context.Diagnostics
        );
    }

    private static Document MakePartial(Document document, SyntaxNode root, List<TypeDeclarationSyntax> declarations)
    {
        var generator = SyntaxGenerator.GetGenerator(document);
        SyntaxNode newRoot = root.ReplaceNodes(
            declarations,
            (_, rewritten) => generator.WithModifiers(rewritten, generator.GetModifiers(rewritten).WithPartial(true))
        );
        return document.WithSyntaxRoot(newRoot);
    }
}
