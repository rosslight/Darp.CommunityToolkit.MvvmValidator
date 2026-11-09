using System.ComponentModel.DataAnnotations;
using CommunityToolkit.Mvvm.ComponentModel;
using Darp.MvvmValidator;

namespace Darp.MvvmValidator.Tests;

public sealed class NotAdminAttribute : ValidationAttribute
{
    public override bool IsValid(object? value) =>
        !string.Equals(value as string, "admin", StringComparison.OrdinalIgnoreCase);
}

public sealed partial class AttributeCoverageViewModel : ValidatingObservableObject
{
    public AttributeCoverageViewModel() => ValidateAllProperties();

    [ObservableProperty]
    [Required]
    [StringLength(20, MinimumLength = 3)]
    [NotAdmin(ErrorMessage = "This user name is reserved.")]
    [Display(Name = "User name")]
    private string? _userName;

    [ObservableProperty]
    [Required]
    [EmailAddress]
    public partial string? Email { get; set; }

    [ObservableProperty]
    [Required]
    [MinLength(8)]
    [MaxLength(64)]
    public partial string? Password { get; set; }

    [ObservableProperty]
    [Compare(nameof(Password), ErrorMessage = "{0} must match {1}.")]
    [Display(Name = "Repeat password")]
    public partial string? ConfirmPassword { get; set; }

    [ObservableProperty]
    [Range(18, 120)]
    public partial int Age { get; set; } = 18;

    [ObservableProperty]
    [Range(0.5, 2.5)]
    public partial double Factor { get; set; } = 1;

    [ObservableProperty]
    [Phone]
    public partial string? Phone { get; set; }

    [ObservableProperty]
    [Url]
    public partial string? Homepage { get; set; }

    [ObservableProperty]
    [RegularExpression("^[A-Z]{2}$")]
    public partial string? CountryCode { get; set; }

    [ObservableProperty]
    [AllowedValues("red", "green")]
    public partial string? Color { get; set; } = "red";

    [ObservableProperty]
    [DeniedValues("root")]
    [Length(1, 10)]
    public partial string? Alias { get; set; } = "me";

    [ObservableProperty]
    [Base64String]
    public partial string? Token { get; set; }

    [MinLength(1)]
    public System.Collections.ICollection Tags
    {
        get;
        set => SetProperty(ref field, value);
    } = new[] { "new" };

    [CustomValidation(typeof(AttributeCoverageViewModel), nameof(ValidateInvite))]
    public string? InviteCode
    {
        get;
        set => SetProperty(ref field, value);
    }

    public static ValidationResult? ValidateInvite(string? code, ValidationContext context) =>
        code is null or "WELCOME"
            ? ValidationResult.Success
            : new ValidationResult($"{context.DisplayName} is unknown.");
}
