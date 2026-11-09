namespace Darp.MvvmValidator.Tests;

/// <summary>
/// Resource-based messages and display names are read with reflection that the trimmer can prove. Published with
/// Native AOT in CI, these tests fail if the resource properties are trimmed away.
/// </summary>
public sealed class ResourceTests
{
    [Fact]
    public void ErrorMessageResource_ShouldBeReadFromPublicProperty()
    {
        var viewModel = new ResourceViewModel { UserName = "abc" };

        viewModel.UserName = null;

        viewModel
            .GetErrors(nameof(viewModel.UserName))
            .Single()
            .ErrorMessage.ShouldBe("User name (resource) is missing (resource).");
    }

    [Fact]
    public void ErrorMessageResource_ShouldBeReadFromInternalProperty()
    {
        var viewModel = new ResourceViewModel { UserName = "abcd" };

        viewModel
            .GetErrors(nameof(viewModel.UserName))
            .Single()
            .ErrorMessage.ShouldBe("User name (resource) is too long (internal resource).");
    }

    [Fact]
    public void CompareResource_ShouldBeReadWithoutTheAttribute()
    {
        var viewModel = new ResourceViewModel { UserName = "abc" };

        viewModel.RepeatedUserName = "xyz";

        viewModel
            .GetErrors(nameof(viewModel.RepeatedUserName))
            .Single()
            .ErrorMessage.ShouldBe("RepeatedUserName differs from User name (resource) (resource).");
    }

    [Fact]
    public void CustomValidationResource_ShouldApplyToAResultWithoutMessage()
    {
        var viewModel = new ResourceViewModel();

        viewModel.Code = "x";

        viewModel
            .GetErrors(nameof(viewModel.Code))
            .Single()
            .ErrorMessage.ShouldBe("Code is invalid (internal resource).");
    }
}
