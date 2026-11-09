using System.Collections.Immutable;
using Darp.MvvmValidator.CodeFixers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Darp.MvvmValidator.Generator.Tests;

public sealed class MakeTypePartialCodeFixerTests
{
    private const string Usings = """
        using System.ComponentModel.DataAnnotations;
        using Darp.MvvmValidator;

        """;

    private static async Task<string> FixAsync(string source)
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        using var workspace = new AdhocWorkspace();
        Document document = workspace
            .AddProject("Test", LanguageNames.CSharp)
            .WithMetadataReferences(TestCompilation.References)
            .WithCompilationOptions(new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary))
            .AddDocument("Test.cs", Usings + source);
        Compilation compilation =
            await document.Project.GetCompilationAsync(cancellationToken)
            ?? throw new InvalidOperationException("The project has no compilation");
        ImmutableArray<Diagnostic> diagnostics = await compilation
            .WithAnalyzers([new ValidationAnalyzer()])
            .GetAnalyzerDiagnosticsAsync(cancellationToken);

        var actions = new List<CodeAction>();
        var context = new CodeFixContext(
            document,
            diagnostics.Single(d => d.Id == "DMV0001"),
            (action, _) => actions.Add(action),
            cancellationToken
        );
        await new MakeTypePartialCodeFixer().RegisterCodeFixesAsync(context);
        ImmutableArray<CodeActionOperation> operations = await actions.Single().GetOperationsAsync(cancellationToken);
        Document fixedDocument =
            operations.OfType<ApplyChangesOperation>().Single().ChangedSolution.GetDocument(document.Id)
            ?? throw new InvalidOperationException("The fix removed the document");
        string text = (await fixedDocument.GetTextAsync(cancellationToken)).ToString();
        return text.Substring(Usings.Length);
    }

    [Fact]
    public async Task NonPartialType_ShouldBecomePartial()
    {
        string fixedSource = await FixAsync(
            """
            public sealed class ViewModel : ValidatingObservableObject
            {
                [Required]
                public string? Name { get; set; }
            }
            """
        );

        fixedSource.ShouldBe(
            """
            public sealed partial class ViewModel : ValidatingObservableObject
            {
                [Required]
                public string? Name { get; set; }
            }
            """
        );
    }

    [Fact]
    public async Task NestedType_ShouldMakeAllContainingTypesPartial()
    {
        string fixedSource = await FixAsync(
            """
            public static class Outer
            {
                public partial class Middle
                {
                    public class ViewModel : ValidatingObservableObject
                    {
                        [Required]
                        public string? Name { get; set; }
                    }
                }
            }
            """
        );

        fixedSource.ShouldBe(
            """
            public static partial class Outer
            {
                public partial class Middle
                {
                    public partial class ViewModel : ValidatingObservableObject
                    {
                        [Required]
                        public string? Name { get; set; }
                    }
                }
            }
            """
        );
    }
}
