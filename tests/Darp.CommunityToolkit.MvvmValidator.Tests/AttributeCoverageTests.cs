namespace Darp.MvvmValidator.Tests;

/// <summary>
/// Runs the built-in validation attributes through generated code. Published with Native AOT in CI, this proves they
/// work without the reflection metadata the trimmer removes.
/// </summary>
public sealed class AttributeCoverageTests
{
    [Fact]
    public void InvalidValues_ShouldReportAnErrorForEveryAttribute()
    {
        var viewModel = new AttributeCoverageViewModel
        {
            UserName = "admin",
            Email = "not-an-email",
            Password = "short",
            ConfirmPassword = "different",
            Age = 5,
            Factor = 3,
            Phone = "abc",
            Homepage = "nope",
            CountryCode = "deu",
            Color = "blue",
            Alias = "root",
            Token = "%%%",
            Tags = Array.Empty<string>(),
            InviteCode = "HELLO",
        };

        // InviteCode is missing: a [CustomValidation] result keeps the member names its method returns, here none
        viewModel
            .GetErrors()
            .SelectMany(e => e.MemberNames)
            .Order(StringComparer.Ordinal)
            .ShouldBe([
                "Age",
                "Alias",
                "Color",
                "ConfirmPassword",
                "CountryCode",
                "Email",
                "Factor",
                "Homepage",
                "Password",
                "Phone",
                "Tags",
                "Token",
                "UserName",
            ]);
        viewModel.GetErrors(nameof(viewModel.UserName)).Single().ErrorMessage.ShouldBe("This user name is reserved.");
        viewModel
            .GetErrors(nameof(viewModel.ConfirmPassword))
            .Single()
            .ErrorMessage.ShouldBe("Repeat password must match Password.");
        viewModel.GetErrors(nameof(viewModel.InviteCode)).Single().ErrorMessage.ShouldBe("InviteCode is unknown.");
    }

    [Fact]
    public void ValidValues_ShouldReportNoErrors()
    {
        var viewModel = new AttributeCoverageViewModel
        {
            UserName = "alice",
            Email = "alice@example.com",
            Password = "long enough",
            ConfirmPassword = "long enough",
            Age = 30,
            Factor = 2,
            Phone = "+49 123 456",
            Homepage = "https://example.com",
            CountryCode = "DE",
            Color = "green",
            Alias = "al",
            Token = "aGVsbG8=",
            InviteCode = "WELCOME",
        };

        viewModel.HasErrors.ShouldBeFalse();
    }
}
