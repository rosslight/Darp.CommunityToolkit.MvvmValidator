using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Darp.MvvmValidator.Generator.Tests;

/// <summary> Every view model the generator cannot handle gets a diagnostic instead of a compile error in generated code </summary>
public sealed class ValidationAnalyzerTests
{
    private const string Usings = """
        using System;
        using System.ComponentModel.DataAnnotations;
        using CommunityToolkit.Mvvm.ComponentModel;
        using Darp.MvvmValidator;

        """;

    private static async Task<string[]> GetDiagnosticIdsAsync(string source)
    {
        ImmutableArray<Diagnostic> diagnostics = await TestCompilation
            .Create(Usings + source)
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

                public static ValidationResult? Check(in string? value) => null;

                [AttributeUsage(AttributeTargets.Property)]
                private sealed class PrivateAttribute : ValidationAttribute;

                [Private]
                public string? UsesPrivateAttribute { get; set; }
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
    public async Task FileLocalType_ShouldReportDMV0001()
    {
        string[] ids = await GetDiagnosticIdsAsync(
            """
            file partial class ViewModel : ValidatingObservableObject
            {
                [Required]
                public string? Name { get; set; }
            }
            """
        );

        ids.ShouldBe(["DMV0001"]);
    }

    [Theory]
    [InlineData("private static ValidationResult? Check(string? value) => null;")]
    [InlineData("public static ValidationResult? Check(ref string? value) => null;")]
    public async Task CustomValidationWithUncallableMethod_ShouldReportDMV0002(string method)
    {
        string[] ids = await GetDiagnosticIdsAsync(
            $$"""
            public partial class ViewModel : ValidatingObservableObject
            {
                [CustomValidation(typeof(ViewModel), nameof(Check))]
                public string? Name { get; set; }

                {{method}}
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

                [ObservableProperty]
                [property: Required]
                private string? _title;
            }
            """
        );

        ids.ShouldBe(["DMV0003", "DMV0003"]);
    }

    [Theory]
    [InlineData("""[Compare("Missing")] public string? Name { get; set; }""")]
    [InlineData("""[Compare(nameof(BaseViewModel.Hidden))] public string? Name { get; set; }""")]
    public async Task CompareWithUnreadableTarget_ShouldReportDMV0004(string members)
    {
        string[] ids = await GetDiagnosticIdsAsync(
            $$"""
            public partial class BaseViewModel : ValidatingObservableObject
            {
                public string? Hidden { private get; set; }
            }

            public partial class ViewModel : BaseViewModel
            {
                {{members}}
            }
            """
        );

        ids.ShouldBe(["DMV0004"]);
    }

    [Theory]
    [InlineData("[Required] public Span<int> Name => default;")]
    [InlineData("[Required] public dynamic? Name { get; set; }")]
    [InlineData("[Required] public unsafe int* Name => null;")]
    [InlineData("[ObservableProperty] [property: AllowedValues(new object[0])] private string? _name;")]
    public async Task UnsupportedProperty_ShouldReportDMV0005(string member)
    {
        string[] ids = await GetDiagnosticIdsAsync(
            $$"""
            public partial class ViewModel : ValidatingObservableObject
            {
                {{member}}
            }
            """
        );

        ids.ShouldBe(["DMV0005"]);
    }
}
