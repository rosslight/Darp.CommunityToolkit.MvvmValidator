using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace Darp.MvvmValidator.Generator;

/// <summary> The symbols the generator and analyzer look for, resolved once per compilation </summary>
internal sealed class KnownSymbols
{
    private static readonly string[] s_countingAttributeNames =
    [
        "System.ComponentModel.DataAnnotations.MinLengthAttribute",
        "System.ComponentModel.DataAnnotations.MaxLengthAttribute",
        "System.ComponentModel.DataAnnotations.LengthAttribute",
    ];

    private KnownSymbols(Compilation compilation)
    {
        ValidatingObservableObject = compilation.GetTypeByMetadataName("Darp.MvvmValidator.ValidatingObservableObject");
        ObservableValidator = compilation.GetTypeByMetadataName(
            "CommunityToolkit.Mvvm.ComponentModel.ObservableValidator"
        );
        ObservablePropertyAttribute = compilation.GetTypeByMetadataName(
            "CommunityToolkit.Mvvm.ComponentModel.ObservablePropertyAttribute"
        );
        ValidationAttribute = compilation.GetTypeByMetadataName(
            "System.ComponentModel.DataAnnotations.ValidationAttribute"
        );
        RequiredAttribute = compilation.GetTypeByMetadataName(
            "System.ComponentModel.DataAnnotations.RequiredAttribute"
        );
        CompareAttribute = compilation.GetTypeByMetadataName("System.ComponentModel.DataAnnotations.CompareAttribute");
        CustomValidationAttribute = compilation.GetTypeByMetadataName(
            "System.ComponentModel.DataAnnotations.CustomValidationAttribute"
        );
        DisplayAttribute = compilation.GetTypeByMetadataName("System.ComponentModel.DataAnnotations.DisplayAttribute");
        ValidationResult = compilation.GetTypeByMetadataName("System.ComponentModel.DataAnnotations.ValidationResult");
        ValidationContext = compilation.GetTypeByMetadataName(
            "System.ComponentModel.DataAnnotations.ValidationContext"
        );
        Collection = compilation.GetTypeByMetadataName("System.Collections.ICollection");
        CountingAttributes = s_countingAttributeNames
            .Select(compilation.GetTypeByMetadataName)
            .OfType<INamedTypeSymbol>()
            .ToImmutableArray();
    }

    public INamedTypeSymbol? ValidatingObservableObject { get; }
    public INamedTypeSymbol? ObservableValidator { get; }
    public INamedTypeSymbol? ObservablePropertyAttribute { get; }
    public INamedTypeSymbol? ValidationAttribute { get; }
    public INamedTypeSymbol? RequiredAttribute { get; }
    public INamedTypeSymbol? CompareAttribute { get; }
    public INamedTypeSymbol? CustomValidationAttribute { get; }
    public INamedTypeSymbol? DisplayAttribute { get; }
    public INamedTypeSymbol? ValidationResult { get; }
    public INamedTypeSymbol? ValidationContext { get; }
    public INamedTypeSymbol? Collection { get; }

    /// <summary> Attributes whose constructor requires unreferenced code because they may reflect on a Count property </summary>
    public ImmutableArray<INamedTypeSymbol> CountingAttributes { get; }

    public static KnownSymbols Create(Compilation compilation) => new(compilation);

    public bool IsValidatingObservableObject(INamedTypeSymbol type) =>
        InheritsFrom(type.BaseType, ValidatingObservableObject);

    public bool IsObservableProperty(ISymbol member) =>
        member
            .GetAttributes()
            .Any(a => SymbolEqualityComparer.Default.Equals(a.AttributeClass, ObservablePropertyAttribute));

    /// <summary>
    /// Whether the IL2026 of the <paramref name="attributeClass"/> constructor can be suppressed for a property of
    /// <paramref name="propertyType"/>, because the generated code never reaches the reflection it warns about.
    /// [Compare] is implemented without the attribute. Length attributes only reflect on values that are neither
    /// strings nor collections.
    /// </summary>
    public bool CanSuppressTrimWarning(INamedTypeSymbol attributeClass, ITypeSymbol propertyType)
    {
        if (SymbolEqualityComparer.Default.Equals(attributeClass, CompareAttribute))
            return true;
        if (!CountingAttributes.Contains(attributeClass, SymbolEqualityComparer.Default))
            return false;
        return propertyType.SpecialType == SpecialType.System_String
            || propertyType.TypeKind == TypeKind.Array
            || (
                Collection is not null
                && (
                    SymbolEqualityComparer.Default.Equals(propertyType, Collection)
                    || propertyType.AllInterfaces.Contains(Collection, SymbolEqualityComparer.Default)
                )
            );
    }

    public static bool InheritsFrom(ITypeSymbol? type, INamedTypeSymbol? baseType)
    {
        if (baseType is null)
            return false;
        for (ITypeSymbol? current = type; current is not null; current = current.BaseType)
        {
            if (SymbolEqualityComparer.Default.Equals(current, baseType))
                return true;
        }
        return false;
    }
}
