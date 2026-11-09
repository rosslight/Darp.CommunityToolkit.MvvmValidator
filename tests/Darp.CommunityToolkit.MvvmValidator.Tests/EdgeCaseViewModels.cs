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

public partial class DisplayOverridingViewModel : OverriddenBaseViewModel
{
    [Display(Name = "Login")]
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
    [Counting]
    public virtual string? Counted { get; set; }

    [Required]
    public string? Hidden => null;

    public new void ValidateProperty(string propertyName) => base.ValidateProperty(propertyName);

    public new void ValidateAllProperties() => base.ValidateAllProperties();
}

public partial class InheritanceViewModel : InheritanceBaseViewModel
{
    public override string? Counted { get; set; }

    public new string Hidden => "okay";
}

/// <summary> Property names that once produced the same generated member names </summary>
public partial class CollidingNamesViewModel : ValidatingObservableObject
{
    [MinLength(1)]
    public string? A { get; set; } = "";

    [Required]
    public string? CreateA { get; set; }

    [Required]
    public string? ValidateA { get; set; }

    [Required]
    public string? A_Rule0 { get; set; }

    public new void ValidateAllProperties() => base.ValidateAllProperties();
}

public partial class PrivateAttributeViewModel : ValidatingObservableObject
{
    [Rejected]
    public string? Name { get; set; }

    public new void ValidateProperty(string propertyName) => base.ValidateProperty(propertyName);

    [AttributeUsage(AttributeTargets.Property)]
    private sealed class RejectedAttribute : ValidationAttribute
    {
        public override bool IsValid(object? value) => false;
    }
}

/// <summary> Reads a service and an item that the view model passes to every ValidationContext </summary>
public sealed class ContextValuesAttribute : ValidationAttribute
{
    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext) =>
        new($"{validationContext.GetService(typeof(string))} {validationContext.Items["item"]}");
}

public partial class ServiceViewModel(IServiceProvider serviceProvider, IDictionary<object, object?> items)
    : ValidatingObservableObject(serviceProvider, items)
{
    [ContextValues]
    public string? Name { get; set; }

    public new void ValidateProperty(string propertyName) => base.ValidateProperty(propertyName);
}

public partial class CallerMemberNameViewModel : ValidatingObservableObject
{
    // Validates through [CallerMemberName] without raising PropertyChanged
    [Required]
    public string? Code
    {
        get;
        set
        {
            field = value;
            ValidateProperty();
        }
    }
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

public partial class EmptyDisplayNameViewModel : ValidatingObservableObject
{
    [Required]
    [Display(Name = "")]
    public string? Unnamed { get; set; }

    public new void ValidateProperty(string propertyName) => base.ValidateProperty(propertyName);
}
