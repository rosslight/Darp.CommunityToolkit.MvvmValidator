using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Darp.MvvmValidator.Generator.Tests;

/// <summary> Compiles test sources against the assemblies of the test process, which include the toolkit and the package </summary>
internal static class TestCompilation
{
    public static MetadataReference[] References { get; } =
        (
            AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string
            ?? throw new InvalidOperationException("The runtime does not list its trusted assemblies")
        )
            .Split(Path.PathSeparator)
            .Select(path => (MetadataReference)MetadataReference.CreateFromFile(path))
            .ToArray();

    public static CSharpCompilation Create(string source) =>
        CSharpCompilation.Create(
            "Test",
            [CSharpSyntaxTree.ParseText(source, cancellationToken: TestContext.Current.CancellationToken)],
            References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true)
        );
}
