using Microsoft.CodeAnalysis;

namespace Darp.MvvmValidator.Generator;

internal static class Diagnostics
{
    private const string Category = "Darp.MvvmValidator.Generator";

    public static readonly DiagnosticDescriptor TypeMustBePartial = new(
        "DMV0001",
        "Validated type must be partial",
        "'{0}' has properties with validation attributes, so it and all its containing types must be partial and must not be file-local",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true
    );

    public static readonly DiagnosticDescriptor InvalidCustomValidationMethod = new(
        "DMV0002",
        "Invalid CustomValidation method",
        "[CustomValidation] on '{0}' must reference a single public static method '{1}' on '{2}' that returns ValidationResult and takes the value, optionally followed by a ValidationContext, both by value or 'in'",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true
    );

    public static readonly DiagnosticDescriptor MissingValidationBase = new(
        "DMV0003",
        "Validation attributes require a validating base type",
        "'{0}' has validation attributes, but '{1}' inherits neither ValidatingObservableObject nor ObservableValidator, so they are never validated",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true
    );

    public static readonly DiagnosticDescriptor InvalidCompareTarget = new(
        "DMV0004",
        "Invalid Compare target",
        "[Compare] on '{0}' references '{1}', which is not a readable, accessible instance property of '{2}'",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true
    );

    public static readonly DiagnosticDescriptor UnsupportedValidation = new(
        "DMV0005",
        "Validation cannot be generated",
        "Validation of '{0}' cannot be generated: {1}",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true
    );
}
