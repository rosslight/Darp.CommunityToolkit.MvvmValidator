using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Darp.MvvmValidator.Generator.Tests;

/// <summary> Snapshots of the generated validation, to review every change to the emitted code </summary>
public sealed class GeneratedSourceTests
{
    private static GeneratorDriver RunGenerator(string source) =>
        CSharpGeneratorDriver
            .Create(new ValidationGenerator())
            .RunGenerators(TestCompilation.Create(source), TestContext.Current.CancellationToken);

    [Fact]
    public Task ViewModel_ShouldGenerateValidationForEveryKindOfProperty()
    {
        const string source = """
            using System.ComponentModel.DataAnnotations;
            using CommunityToolkit.Mvvm.ComponentModel;
            using Darp.MvvmValidator;

            namespace Sample;

            public partial class SignUpViewModel : ValidatingObservableObject
            {
                [ObservableProperty]
                [Required]
                [StringLength(20, MinimumLength = 3)]
                [Display(Name = "User name")]
                private string? _userName;

                [ObservableProperty]
                [Required]
                [MinLength(8)]
                public partial string? Password { get; set; }

                [ObservableProperty]
                [property: Compare(nameof(Password))]
                private string? _confirmPassword;

                [CustomValidation(typeof(SignUpViewModel), nameof(ValidateInvite))]
                public string? InviteCode
                {
                    get;
                    set => SetProperty(ref field, value);
                }

                public static ValidationResult? ValidateInvite(string? code, ValidationContext context) => null;
            }
            """;

        return Verify(RunGenerator(source));
    }

    [Fact]
    public void TrimWarnings_ShouldOnlyBeSuppressedForSafeAttributesWithoutHandWrittenAccessors()
    {
        const string source = """
            using System;
            using System.ComponentModel.DataAnnotations;
            using Darp.MvvmValidator;

            namespace Sample;

            public partial class ViewModel : ValidatingObservableObject
            {
                [MinLength(1)]
                public string? Safe { get; set; }

                [MinLength(1)]
                [Range(typeof(DateTime), "2020-01-01", "2030-01-01")]
                public string? UnsafeAttribute { get; set; }

                // The suppression would also cover this setter
                [MinLength(1)]
                public string? HandWrittenSetter
                {
                    get;
                    set => field = value;
                }
            }
            """;

        string generated = string.Concat(
            RunGenerator(source)
                .GetRunResult()
                .GeneratedTrees.Select(tree => tree.GetText(TestContext.Current.CancellationToken))
        );

        generated.ShouldContain("Target = \"P:Sample.ViewModel.Safe\"");
        generated.ShouldNotContain("P:Sample.ViewModel.UnsafeAttribute");
        generated.ShouldNotContain("P:Sample.ViewModel.HandWrittenSetter");
    }
}
