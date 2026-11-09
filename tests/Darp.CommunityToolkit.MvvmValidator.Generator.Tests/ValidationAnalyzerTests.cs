using System.Collections.Immutable;
using Darp.MvvmValidator.Generator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Darp.MvvmValidator.Generator.Tests;

public sealed class ValidationAnalyzerTests
{
    private const string Usings = """
        using System.ComponentModel.DataAnnotations;
        using CommunityToolkit.Mvvm.ComponentModel;
        using Darp.MvvmValidator;

        """;

    private static async Task<string[]> GetDiagnosticIdsAsync(string source)
    {
        IEnumerable<MetadataReference> references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Select(path => MetadataReference.CreateFromFile(path));
        CSharpCompilation compilation = CSharpCompilation.Create(
            "Test",
            [CSharpSyntaxTree.ParseText(Usings + source, cancellationToken: TestContext.Current.CancellationToken)],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
        );
        CSharpGeneratorDriver
            .Create(new ValidationGenerator())
            .RunGeneratorsAndUpdateCompilation(
                compilation,
                out Compilation generated,
                out _,
                TestContext.Current.CancellationToken
            );
        ImmutableArray<Diagnostic> diagnostics = await generated
            .WithAnalyzers([new ValidationAnalyzer()])
            .GetAnalyzerDiagnosticsAsync(TestContext.Current.CancellationToken);
        return diagnostics.Select(d => d.Id).ToArray();
    }

    [Fact]
    public async Task ValidType_ShouldHaveNoDiagnostics()
    {
        string[] ids = await GetDiagnosticIdsAsync(
            """
            public partial class ViewModel : ValidatingObservableObject
            {
                [Required]
                [CustomValidation(typeof(ViewModel), nameof(Check))]
                public string? Name { get; set; }

                [Compare(nameof(Name))]
                public string? Repeated { get; set; }

                public static ValidationResult? Check(string? value) => null;
            }
            """
        );

        ids.ShouldBeEmpty();
    }

    [Fact]
    public async Task NonPartialType_ShouldReportDMV0001()
    {
        string[] ids = await GetDiagnosticIdsAsync(
            """
            public class ViewModel : ValidatingObservableObject
            {
                [Required]
                public string? Name { get; set; }
            }
            """
        );

        ids.ShouldBe(["DMV0001"]);
    }

    [Fact]
    public async Task CustomValidationWithPrivateMethod_ShouldReportDMV0002()
    {
        string[] ids = await GetDiagnosticIdsAsync(
            """
            public partial class ViewModel : ValidatingObservableObject
            {
                [CustomValidation(typeof(ViewModel), nameof(Check))]
                public string? Name { get; set; }

                private static ValidationResult? Check(string? value) => null;
            }
            """
        );

        ids.ShouldBe(["DMV0002"]);
    }

    [Fact]
    public async Task ObservablePropertyWithValidationOnObservableObject_ShouldReportDMV0003()
    {
        string[] ids = await GetDiagnosticIdsAsync(
            """
            public partial class ViewModel : ObservableObject
            {
                [ObservableProperty]
                [Required]
                private string? _name;
            }
            """
        );

        ids.ShouldBe(["DMV0003"]);
    }

    [Fact]
    public async Task PropertyTargetedValidationOnObservableObject_ShouldReportDMV0003()
    {
        string[] ids = await GetDiagnosticIdsAsync(
            """
            public partial class ViewModel : ObservableObject
            {
                [ObservableProperty]
                [property: Required]
                private string? _name;
            }
            """
        );

        ids.ShouldBe(["DMV0003"]);
    }

    [Fact]
    public async Task CompareWithMissingResourceProperty_ShouldReportDMV0005()
    {
        string[] ids = await GetDiagnosticIdsAsync(
            """
            public static class Strings
            {
                private static string Hidden => "x";
            }

            public partial class ViewModel : ValidatingObservableObject
            {
                public string? Name { get; set; }

                [Compare(nameof(Name), ErrorMessageResourceType = typeof(Strings), ErrorMessageResourceName = "Hidden")]
                public string? Repeated { get; set; }
            }
            """
        );

        ids.ShouldBe(["DMV0005"]);
    }

    [Fact]
    public async Task CompareWithUnknownProperty_ShouldReportDMV0004()
    {
        string[] ids = await GetDiagnosticIdsAsync(
            """
            public partial class ViewModel : ValidatingObservableObject
            {
                [Compare("Missing")]
                public string? Name { get; set; }
            }
            """
        );

        ids.ShouldBe(["DMV0004"]);
    }
}
