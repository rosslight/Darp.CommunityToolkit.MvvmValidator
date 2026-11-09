using System.Runtime.CompilerServices;

namespace Darp.MvvmValidator.Generator.Tests;

public static class VerifySettingsInitializer
{
    [ModuleInitializer]
    public static void Initialize()
    {
        VerifySourceGenerators.Initialize();
        DerivePathInfo(
            (_, projectDirectory, type, method) =>
                new PathInfo(Path.Combine(projectDirectory, "Snapshots", type.Name), type.Name, method.Name)
        );
    }
}

public sealed class VerifySettingsTests
{
    [Fact]
    public Task RunVerifyChecks() => VerifyChecks.Run();
}
