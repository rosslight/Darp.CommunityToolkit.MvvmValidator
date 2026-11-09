namespace Darp.MvvmValidator.Tests;

/// <summary> Regressions for attribute shapes the generator once reproduced incorrectly </summary>
public sealed class EdgeCaseTests
{
    private static string[] Messages(ValidatingObservableObject viewModel, string propertyName) =>
        viewModel.GetErrors(propertyName).Select(e => e.ErrorMessage ?? "").ToArray();

    [Fact]
    public void Attribute_ShouldBeCreatedWithTheAppliedConstructorOverload()
    {
        var viewModel = new OverloadViewModel();

        viewModel.ValidateProperty(nameof(viewModel.OnProperty));

        Messages(viewModel, nameof(viewModel.OnProperty)).ShouldBe(["object:x"]);
    }

    [Fact]
    public void PropertyTargetedAttributes_ShouldSupportArraysAndNamedConstructorArguments()
    {
        var viewModel = new OverloadViewModel { ArrayArgument = "c", NamedConstructorArgument = "long" };

        Messages(viewModel, nameof(viewModel.ArrayArgument))
            .ShouldBe([
                "The ArrayArgument field does not equal any of the values specified in AllowedValuesAttribute.",
            ]);
        Messages(viewModel, nameof(viewModel.NamedConstructorArgument))
            .ShouldBe(["The field NamedConstructorArgument must be a string with a maximum length of 3."]);
    }

    [Fact]
    public void Override_ShouldKeepValidationAndDisplayAttributesOfTheBaseProperty()
    {
        var viewModel = new OverridingViewModel();

        viewModel.ValidateProperty(nameof(viewModel.Name));
        Messages(viewModel, nameof(viewModel.Name)).ShouldBe(["The User name field is required."]);

        viewModel.Name = "long";
        viewModel.ValidateProperty(nameof(viewModel.Name));
        Messages(viewModel, nameof(viewModel.Name))
            .ShouldBe(["The field User name must be a string with a maximum length of 3."]);
    }

    [Fact]
    public void PropertyTargetedAttributes_ShouldSupportArraysWithoutInitializer()
    {
        var viewModel = new OverloadViewModel { EmptyArrayArgument = "x" };

        viewModel.GetErrors(nameof(viewModel.EmptyArrayArgument)).ShouldHaveSingleItem();
    }

    [Fact]
    public void ValidateAllProperties_ShouldRunInheritedRulesOfAnOverrideOnce()
    {
        var viewModel = new InheritanceViewModel();
        CountingAttribute.Calls = 0;

        viewModel.ValidateAllProperties();

        CountingAttribute.Calls.ShouldBe(1);
    }

    [Fact]
    public void PropertyHiddenWithNew_ShouldNotUseTheRulesOfTheHiddenProperty()
    {
        var viewModel = new InheritanceViewModel();

        viewModel.ValidateProperty(nameof(viewModel.Hidden));

        viewModel.GetErrors(nameof(viewModel.Hidden)).ShouldBeEmpty();
    }

    [Fact]
    public void EmptyDisplayName_ShouldFallBackToThePropertyName()
    {
        var viewModel = new EmptyDisplayNameViewModel();

        viewModel.ValidateProperty(nameof(viewModel.Unnamed));

        Messages(viewModel, nameof(viewModel.Unnamed)).ShouldBe(["The Unnamed field is required."]);
    }

    [Fact]
    public void Compare_ShouldReadStaticTargets()
    {
        var viewModel = new StaticCompareViewModel { Value = "y" };

        Messages(viewModel, nameof(viewModel.Value)).ShouldBe(["'Value' and 'Expected' do not match."]);
    }
}
