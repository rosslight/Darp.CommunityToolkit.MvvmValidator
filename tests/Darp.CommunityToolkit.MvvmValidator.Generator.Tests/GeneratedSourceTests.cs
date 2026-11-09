using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Darp.MvvmValidator.Generator.Tests;

/// <summary> Snapshots of the generated validation, to review every change to the emitted code </summary>
public sealed class GeneratedSourceTests
{
    private static GeneratorDriver RunGenerator(string source)
    {
        IEnumerable<MetadataReference> references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Select(path => MetadataReference.CreateFromFile(path));
        CSharpCompilation compilation = CSharpCompilation.Create(
            "Test",
            [CSharpSyntaxTree.ParseText(source, cancellationToken: TestContext.Current.CancellationToken)],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
        );
        return CSharpGeneratorDriver
            .Create(new ValidationGenerator())
            .RunGenerators(compilation, TestContext.Current.CancellationToken);
    }

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
    public Task NestedGenericType_ShouldRepeatTheTypeHierarchy()
    {
        const string source = """
            using System.ComponentModel.DataAnnotations;
            using Darp.MvvmValidator;

            namespace Sample;

            public partial class Outer
            {
                public partial class Inner<T> : ValidatingObservableObject
                {
                    [Required]
                    public T? Value { get; set; }
                }
            }
            """;

        return Verify(RunGenerator(source));
    }
}
