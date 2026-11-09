using System.ComponentModel.DataAnnotations;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Darp.MvvmValidator.Tests;

/// <summary> Reports which constructor created it, to detect a different overload in generated code </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
public sealed class OverloadAttribute : ValidationAttribute
{
    public OverloadAttribute(object value) => ErrorMessage = $"object:{value}";

    public OverloadAttribute(string value) => ErrorMessage = $"string:{value}";

    public override bool IsValid(object? value) => false;
}

public partial class OverloadViewModel : ValidatingObservableObject
{
    [Overload((object)"x")]
    public string? OnProperty { get; set; }

    [ObservableProperty]
    [property: AllowedValues(new object[] { "a", "b" })]
    private string? _arrayArgument;

    [ObservableProperty]
    [property: StringLength(maximumLength: 3)]
    private string? _namedConstructorArgument;

    [ObservableProperty]
    [property: AllowedValues(new object[0])]
    private string? _emptyArrayArgument;

    public new void ValidateProperty(string propertyName) => base.ValidateProperty(propertyName);
}

public partial class OverriddenBaseViewModel : ValidatingObservableObject
{
    [Required]
    [Display(Name = "User name")]
    public virtual string? Name { get; set; }

    public new void ValidateProperty(string propertyName) => base.ValidateProperty(propertyName);
}

public partial class OverridingViewModel : OverriddenBaseViewModel
{
    [StringLength(3)]
    public override string? Name { get; set; }
}

public interface IHasName
{
    string? Name { get; }
}

/// <summary> Explicit interface properties must not break the generated code </summary>
public partial class ExplicitInterfaceViewModel : ValidatingObservableObject, IHasName
{
    [Required]
    string? IHasName.Name => null;

    [Required]
    public string? Title { get; set; }
}

/// <summary> Escaped type parameter names must not break the generated code </summary>
#pragma warning disable CA1715 // The keyword name is the point of this test
public partial class EscapedGenericViewModel<@event> : ValidatingObservableObject
#pragma warning restore CA1715
{
    [Required]
    public @event? Value { get; set; }
}

[AttributeUsage(AttributeTargets.Property, AllowMultiple = true)]
public sealed class TagAttribute(string tag) : ValidationAttribute(tag)
{
    public string Tag { get; } = tag;

    // Like TypeDescriptor, keep one instance per tag instead of one per type
    public override object TypeId => Tag;

    public override bool IsValid(object? value) => false;
}

public sealed class CountingAttribute : ValidationAttribute
{
    public static int Calls { get; set; }

    public override bool IsValid(object? value)
    {
        Calls++;
        return true;
    }
}

public partial class InheritanceBaseViewModel : ValidatingObservableObject
{
    [Tag("base")]
    public virtual string? Tagged { get; set; }

    [Counting]
    public virtual string? Counted { get; set; }

    [Required]
    public string? Hidden => null;

    public new void ValidateProperty(string propertyName) => base.ValidateProperty(propertyName);

    public new void ValidateAllProperties() => base.ValidateAllProperties();
}

public partial class InheritanceViewModel : InheritanceBaseViewModel
{
    [Tag("derived")]
    public override string? Tagged { get; set; }

    public override string? Counted { get; set; }

    public new string Hidden => "okay";
}

public partial class StaticCompareViewModel : ValidatingObservableObject
{
    public static string Expected => "x";

    [Compare(nameof(Expected))]
    public string? Value
    {
        get;
        set => SetProperty(ref field, value);
    }
}
