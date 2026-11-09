using System.ComponentModel.DataAnnotations;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Darp.MvvmValidator.Tests;

/// <summary>
/// Resources only referenced by name, like a ResX designer class. Code never reads these properties, so they only
/// survive trimming if the attributes' [DynamicallyAccessedMembers] annotations preserve them.
/// </summary>
public static class TestResources
{
    public static string RequiredMessage => "{0} is missing (resource).";

    internal static string LengthMessage => "{0} is too long (internal resource).";

    public static string UserNameDisplay => "User name (resource)";

    public static string MismatchMessage => "{0} differs from {1} (resource).";

    internal static string InvalidMessage => "{0} is invalid (internal resource).";
}

public partial class ResourceViewModel : ValidatingObservableObject
{
    [Required(
        ErrorMessageResourceType = typeof(TestResources),
        ErrorMessageResourceName = nameof(TestResources.RequiredMessage)
    )]
    [StringLength(3, ErrorMessageResourceType = typeof(TestResources), ErrorMessageResourceName = "LengthMessage")]
    [Display(Name = nameof(TestResources.UserNameDisplay), ResourceType = typeof(TestResources))]
    public string? UserName
    {
        get;
        set => SetProperty(ref field, value);
    }

    [Compare(
        nameof(UserName),
        ErrorMessageResourceType = typeof(TestResources),
        ErrorMessageResourceName = nameof(TestResources.MismatchMessage)
    )]
    [ObservableProperty]
    public partial string? RepeatedUserName { get; set; }

    // The method returns a result without a message, so the attribute's message applies
    [CustomValidation(
        typeof(ResourceViewModel),
        nameof(RejectAll),
        ErrorMessageResourceType = typeof(TestResources),
        ErrorMessageResourceName = "InvalidMessage"
    )]
    public string? Code
    {
        get;
        set => SetProperty(ref field, value);
    }

    public static ValidationResult? RejectAll(string? value) => new(null);
}
