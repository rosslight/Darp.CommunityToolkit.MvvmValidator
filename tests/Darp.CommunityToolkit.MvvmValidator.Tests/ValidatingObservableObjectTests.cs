using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace Darp.MvvmValidator.Tests;

public sealed class ValidatingObservableObjectTests
{
    private static string[] Messages(ValidatingObservableObject viewModel, string propertyName) =>
        viewModel.GetErrors(propertyName).Select(e => e.ErrorMessage ?? "").ToArray();

    [Fact]
    public void SettingObservablePropertyField_ShouldValidate_WithRequiredFirstAndDisplayName()
    {
        var viewModel = new RegistrationViewModel();

        viewModel.UserName = "";
        Messages(viewModel, nameof(viewModel.UserName)).ShouldBe(["The User name field is required."]);

        viewModel.UserName = "ab";
        Messages(viewModel, nameof(viewModel.UserName))
            .ShouldBe(["The field User name must be a string with a minimum length of 3 and a maximum length of 20."]);

        viewModel.UserName = "abc";
        viewModel.GetErrors(nameof(viewModel.UserName)).ShouldBeEmpty();
    }

    [Fact]
    public void SettingPartialAndHandWrittenProperties_ShouldValidate()
    {
        var viewModel = new RegistrationViewModel { Email = "no-mail", Age = 5 };

        Messages(viewModel, nameof(viewModel.Email)).ShouldBe(["The Email field is not a valid e-mail address."]);
        Messages(viewModel, nameof(viewModel.Age)).ShouldBe(["The field Age must be between 18 and 120."]);
    }

    [Fact]
    public void CustomValidation_ShouldCallMethodWithContext()
    {
        var viewModel = new RegistrationViewModel
        {
            NicknameCallback = nickname => nickname == "admin" ? new ValidationResult("Reserved") : null,
        };

        viewModel.Nickname = "admin";
        Messages(viewModel, nameof(viewModel.Nickname)).ShouldBe(["Reserved"]);

        viewModel.Nickname = "bob";
        viewModel.GetErrors(nameof(viewModel.Nickname)).ShouldBeEmpty();
    }

    [Fact]
    public void Compare_ShouldUseValueOfOtherProperty()
    {
        var viewModel = new RegistrationViewModel { Password = "secret" };

        viewModel.ConfirmPassword = "other";
        Messages(viewModel, nameof(viewModel.ConfirmPassword))
            .ShouldBe(["'ConfirmPassword' and 'Password' do not match."]);

        viewModel.ConfirmPassword = "secret";
        viewModel.GetErrors(nameof(viewModel.ConfirmPassword)).ShouldBeEmpty();
    }

    [Fact]
    public void ChangingCompareTarget_ShouldValidateTheComparingPropertyAgain()
    {
        var viewModel = new RegistrationViewModel { Password = "secret", ConfirmPassword = "other" };
        viewModel.GetErrors(nameof(viewModel.ConfirmPassword)).ShouldNotBeEmpty();

        viewModel.Password = "other";
        viewModel.GetErrors(nameof(viewModel.ConfirmPassword)).ShouldBeEmpty();

        viewModel.Password = "changed";
        viewModel.GetErrors(nameof(viewModel.ConfirmPassword)).ShouldNotBeEmpty();
    }

    [Fact]
    public void ChangingCompareTarget_ShouldNotValidateAnUntouchedComparingProperty()
    {
        var viewModel = new RegistrationViewModel();

        viewModel.Password = "secret";

        viewModel.GetErrors(nameof(viewModel.ConfirmPassword)).ShouldBeEmpty();
    }

    [Fact]
    public void ErrorsAndHasErrors_ShouldBeUpToDate_WhenPropertyChangedIsRaised()
    {
        var viewModel = new RegistrationViewModel();
        var events = new List<string>();
        viewModel.ErrorsChanged += (_, e) => events.Add($"ErrorsChanged:{e.PropertyName}");
        viewModel.PropertyChanged += (_, e) =>
            events.Add($"PropertyChanged:{e.PropertyName}:{viewModel.GetErrors(e.PropertyName).Count()}");

        viewModel.UserName = "";

        events.ShouldBe(["ErrorsChanged:UserName", "PropertyChanged:HasErrors:0", "PropertyChanged:UserName:1"]);
        viewModel.HasErrors.ShouldBeTrue();
    }

    [Fact]
    public void ErrorsChanged_ShouldNotBeRaised_WhenMessagesStayTheSame()
    {
        var viewModel = new RegistrationViewModel { UserName = "a" };
        var raised = 0;
        viewModel.ErrorsChanged += (_, _) => raised++;

        viewModel.UserName = "b";

        raised.ShouldBe(0);
    }

    [Fact]
    public void ValidateAllProperties_ShouldValidateInitialValues_IncludingBaseTypes()
    {
        var viewModel = new AddressViewModel();
        viewModel.HasErrors.ShouldBeFalse();

        viewModel.ValidateAllProperties();

        viewModel
            .GetErrors()
            .SelectMany(e => e.MemberNames)
            .Order()
            .ShouldBe(["Email", "Password", "Street", "UserName"]);
    }

    [Fact]
    public void GenericNestedType_ShouldValidate()
    {
        var viewModel = new Outer.GenericViewModel<string> { Value = "x" };

        viewModel.Value = null;

        Messages(viewModel, nameof(viewModel.Value)).ShouldBe(["The Value field is required."]);
    }

    [Fact]
    public void SetErrors_ShouldPublishExternalResults_UntilTheNextValidation()
    {
        var viewModel = new RegistrationViewModel { UserName = "valid" };

        viewModel.SetErrors(nameof(viewModel.UserName), [new ValidationResult("Taken")]);
        Messages(viewModel, nameof(viewModel.UserName)).ShouldBe(["Taken"]);
        viewModel.HasErrors.ShouldBeTrue();

        viewModel.UserName = "other";
        viewModel.HasErrors.ShouldBeFalse();
    }

    [Fact]
    public void SetErrors_ShouldReplaceResultsWithTheSameMessage_WithoutRaisingErrorsChanged()
    {
        var viewModel = new RegistrationViewModel { UserName = "valid" };
        viewModel.SetErrors(nameof(viewModel.UserName), [new ValidationResult("Taken", ["Old"])]);
        var raised = 0;
        viewModel.ErrorsChanged += (_, _) => raised++;

        viewModel.SetErrors(nameof(viewModel.UserName), [new ValidationResult("Taken", ["New"])]);

        viewModel.GetErrors(nameof(viewModel.UserName)).Single().MemberNames.ShouldBe(["New"]);
        raised.ShouldBe(0);
    }

    [Fact]
    public void ClearErrors_ShouldRemoveAllErrors()
    {
        var viewModel = new RegistrationViewModel();
        viewModel.ValidateAllProperties();

        viewModel.ClearErrors();

        viewModel.HasErrors.ShouldBeFalse();
    }

    [Fact]
    public void ClearErrors_ShouldForgetValidatedProperties_SoAResetFormStaysClean()
    {
        var viewModel = new RegistrationViewModel { Password = "secret", ConfirmPassword = "other" };
        viewModel.ClearErrors();

        viewModel.Password = "changed";

        viewModel.GetErrors(nameof(viewModel.ConfirmPassword)).ShouldBeEmpty();
    }

    [Fact]
    public void ClearErrors_ForOneProperty_ShouldKeepTheErrorsOfOtherProperties()
    {
        var viewModel = new RegistrationViewModel();
        viewModel.ValidateAllProperties();

        viewModel.ClearErrors(nameof(viewModel.UserName));

        viewModel.GetErrors(nameof(viewModel.UserName)).ShouldBeEmpty();
        viewModel.GetErrors(nameof(viewModel.Email)).ShouldNotBeEmpty();
    }

    [Fact]
    public void ValidateProperty_ShouldDefaultToTheCallingProperty()
    {
        var viewModel = new CallerMemberNameViewModel();

        viewModel.Code = null;

        viewModel.GetErrors(nameof(viewModel.Code)).ShouldHaveSingleItem();
    }

    [Fact]
    public void ValidateProperty_ShouldThrow_ForPropertyWithoutValidation()
    {
        var viewModel = new RegistrationViewModel();

        Should.Throw<ArgumentException>(() => viewModel.ValidateProperty(nameof(viewModel.NicknameCallback)));
    }

    [Fact]
    public void GetErrors_ShouldBeUsableThroughINotifyDataErrorInfo()
    {
        INotifyDataErrorInfo viewModel = new RegistrationViewModel { UserName = "" };

        viewModel.GetErrors("UserName").Cast<ValidationResult>().ShouldHaveSingleItem();
    }
}
