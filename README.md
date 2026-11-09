# Darp.CommunityToolkit.MvvmValidator

[![NuGet](https://img.shields.io/nuget/v/Darp.CommunityToolkit.MvvmValidator.svg)](https://www.nuget.org/packages/Darp.CommunityToolkit.MvvmValidator)
[![Downloads](https://img.shields.io/nuget/dt/Darp.CommunityToolkit.MvvmValidator)](https://www.nuget.org/packages/Darp.CommunityToolkit.MvvmValidator)
![License](https://img.shields.io/github/license/rosslight/Darp.CommunityToolkit.MvvmValidator)
![.NET](https://img.shields.io/badge/version-.NET10-blue)

An AOT- and trimming-compatible replacement for `ObservableValidator` from
[CommunityToolkit.Mvvm](https://github.com/CommunityToolkit/dotnet). It builds on the released toolkit; no fork needed.

`ValidatingObservableObject` derives from `ObservableObject` and implements `INotifyDataErrorInfo`. A source generator
writes the validation for each derived type, so no reflection runs at runtime.

```csharp
using System.ComponentModel.DataAnnotations;
using CommunityToolkit.Mvvm.ComponentModel;
using Darp.MvvmValidator;

public partial class SignUpViewModel : ValidatingObservableObject
{
    public SignUpViewModel() => ValidateAllProperties();

    [ObservableProperty]
    [Required]
    [StringLength(20, MinimumLength = 3)]
    [Display(Name = "User name")]
    public partial string? UserName { get; set; }

    [ObservableProperty]
    [Required]
    public partial string? Password { get; set; }

    [ObservableProperty]
    [Compare(nameof(Password))]
    public partial string? ConfirmPassword { get; set; }

    [CustomValidation(typeof(SignUpViewModel), nameof(ValidateInvite))]
    public string? InviteCode
    {
        get;
        set => SetProperty(ref field, value);
    }

    public static ValidationResult? ValidateInvite(string? code, ValidationContext context) =>
        code is null or "WELCOME" ? ValidationResult.Success : new ValidationResult("Unknown invite code");
}
```

## Behavior

- Every property with validation attributes is validated whenever it raises `PropertyChanged`, before subscribers
  are notified. This covers `[ObservableProperty]` fields and partial properties as well as hand-written `SetProperty`
  setters. `[NotifyDataErrorInfo]` and `SetProperty(..., validate: true)` are not needed.
- Validation runs like `Validator.TryValidateProperty`: `[Required]` runs first and stops validation if it fails,
  then all other attributes run.
- `ValidateAllProperties()` validates the current values, for example from the constructor.
- `SetErrors(propertyName, errors)` publishes results from asynchronous or server-side validation.
  The next validation of that property replaces them.
- `[CustomValidation]` calls the method directly, and `[Compare]` reads the other property directly.
  Both work without reflection.
- Messages and display names can come from resources (`ErrorMessageResourceType`/`ErrorMessageResourceName`,
  `[Display(ResourceType = ...)]`). The resource properties are read with reflection that the trimmer can prove through
  `[DynamicallyAccessedMembers]`, so they also work with Native AOT.
- When the target of a `[Compare]` changes, the comparing property is validated again, but only if it was validated
  before. An untouched "repeat password" field stays free of errors while the user types the password.

## Requirements

- .NET 10. Older frameworks don't have a trim-safe `ValidationContext` constructor.
- Types deriving from `ValidatingObservableObject` and their containing types must be `partial`.
- Attributes that can only target properties, like `[Compare]`, need the `property:` target on `[ObservableProperty]`
  fields: `[property: Compare(nameof(Password))]`.

## Diagnostics

The package turns off the toolkit's MVVMTK0006, which only accepts `ObservableValidator`, and reports these instead:

| Id | Meaning |
| --- | --- |
| DMV0001 | A validated type or one of its containing types is not partial |
| DMV0002 | `[CustomValidation]` doesn't reference a single public static method with a matching signature |
| DMV0003 | An `[ObservableProperty]` member has validation attributes, but its type inherits neither `ValidatingObservableObject` nor `ObservableValidator` |
| DMV0004 | `[Compare]` references a member that isn't a readable property |
| DMV0005 | An attribute argument can't be reproduced in generated code, or the resource message of a `[Compare]`/`[CustomValidation]` doesn't name a static string property |

The trim analyzer reports IL2026 where `[Compare]`, `[MinLength]`, `[MaxLength]` or `[Length]` is applied.
The package suppresses these warnings when the generated code never reaches the reflection they warn about.

## Preview packages

Every pull request publishes a preview package to the GitHub Packages feed
`https://nuget.pkg.github.com/rosslight/index.json`. The pull request comment names the version. The feed requires a
GitHub token with `read:packages` access.

## Development

- Format with [CSharpier](https://csharpier.com): `dotnet tool restore` and `dotnet csharpier format .`
- `tests/Darp.CommunityToolkit.MvvmValidator.Tests` runs the runtime behavior. CI also publishes it with Native AOT and
  `/warnaserror`, which proves the generated validation is trim safe:
  `dotnet publish tests/Darp.CommunityToolkit.MvvmValidator.Tests -c Release -r win-x64 /warnaserror`
- `tests/Darp.CommunityToolkit.MvvmValidator.Generator.Tests` covers the analyzer and snapshots the generated code
  with [Verify](https://github.com/VerifyTests/Verify). Review changed snapshots before accepting them.
- Releases are cut by [release-please](https://github.com/googleapis/release-please) from
  [Conventional Commits](https://www.conventionalcommits.org) on `main`.
