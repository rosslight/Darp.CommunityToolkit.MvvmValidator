using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Darp.MvvmValidator.Generator;

/// <summary> The validation of a type deriving from ValidatingObservableObject </summary>
/// <param name="Type"> The type </param>
/// <param name="IsPartial"> Whether the type and all containing types are partial </param>
/// <param name="Properties"> The properties declared in this type that have validation attributes </param>
/// <param name="Diagnostics"> Everything that prevents generating the validation </param>
internal sealed record TypeValidation(
    INamedTypeSymbol Type,
    bool IsPartial,
    ImmutableArray<ValidatedProperty> Properties,
    ImmutableArray<Diagnostic> Diagnostics
);

/// <summary> A property with validation attributes </summary>
/// <param name="Name"> The property name. For an [ObservableProperty] field, the name of the generated property </param>
/// <param name="Display"> The [Display] attribute, if any </param>
/// <param name="Rules"> The rules in execution order. A required rule always comes first </param>
/// <param name="IsValidatedByBase">
/// Whether a base type validates a property with this name, so its ValidateAllProperties already covers it
/// </param>
internal sealed record ValidatedProperty(
    string Name,
    AttributeInfo? Display,
    ImmutableArray<PropertyRule> Rules,
    bool IsValidatedByBase
);

internal abstract record PropertyRule;

/// <summary> Runs a static instance of the validation attribute </summary>
/// <param name="Attribute"> The attribute </param>
/// <param name="IsRequired"> Whether a failure stops validating the property </param>
/// <param name="IsTrimSafe"> Whether the attribute constructor requires unreferenced code that this property never needs </param>
internal sealed record AttributeRule(AttributeInfo Attribute, bool IsRequired, bool IsTrimSafe) : PropertyRule;

/// <summary> Calls the method of a [CustomValidation] directly instead of looking it up with reflection </summary>
internal sealed record CustomValidationRule(string Method, bool PassesContext, string? ErrorMessage) : PropertyRule;

/// <summary> Implements [Compare] by reading the other property directly instead of with reflection </summary>
/// <param name="OtherProperty"> The name of the compared property </param>
/// <param name="OtherValue"> The C# expression reading the compared property </param>
/// <param name="OtherDisplay"> The [Display] attribute of the compared property, if any </param>
/// <param name="ErrorMessage"> The <c>ErrorMessage</c> set on the attribute </param>
internal sealed record CompareRule(
    string OtherProperty,
    string OtherValue,
    AttributeInfo? OtherDisplay,
    string? ErrorMessage
) : PropertyRule;

internal static class ValidationModel
{
    public static TypeValidation Build(
        INamedTypeSymbol type,
        KnownSymbols known,
        Compilation compilation,
        CancellationToken cancellationToken
    )
    {
        var context = new BuildContext(type, known, compilation);
        var properties = ImmutableArray.CreateBuilder<ValidatedProperty>();
        foreach (ISymbol member in type.GetMembers())
        {
            cancellationToken.ThrowIfCancellationRequested();
            ValidatedProperty? property = member switch
            {
                // Explicit interface implementations cannot be referenced by name, so they never raise PropertyChanged
                IPropertySymbol
                {
                    IsStatic: false,
                    IsIndexer: false,
                    CanBeReferencedByName: true,
                    GetMethod: not null,
                } p => CreateProperty(context, p.Name, p.Type, p, IsValidatedByBase(context, p.Name)),
                IFieldSymbol { IsStatic: false } f when known.IsObservableProperty(f) => CreateProperty(
                    context,
                    GetGeneratedPropertyName(f.Name),
                    f.Type,
                    f,
                    isValidatedByBase: false
                ),
                _ => null,
            };
            if (property is not null)
                properties.Add(property);
        }

        bool isPartial = IsPartialHierarchy(type);
        if (!isPartial && properties.Count > 0)
        {
            context.Diagnostics.Add(
                Diagnostic.Create(Diagnostics.TypeMustBePartial, type.Locations.FirstOrDefault(), type.Name)
            );
        }
        return new TypeValidation(type, isPartial, properties.ToImmutable(), context.Diagnostics.ToImmutable());
    }

    /// <summary> Mirrors how CommunityToolkit.Mvvm names the property generated for an [ObservableProperty] field </summary>
    public static string GetGeneratedPropertyName(string fieldName)
    {
        string name = fieldName;
        if (name.StartsWith("m_", StringComparison.Ordinal))
            name = name.Substring(2);
        else if (name.StartsWith("_", StringComparison.Ordinal))
            name = name.TrimStart('_');
        return name.Length == 0 ? fieldName : char.ToUpper(name[0], CultureInfo.InvariantCulture) + name.Substring(1);
    }

    private static ValidatedProperty? CreateProperty(
        BuildContext context,
        string name,
        ITypeSymbol propertyType,
        ISymbol member,
        bool isValidatedByBase
    )
    {
        var rules = new List<PropertyRule>();
        AttributeInfo? display = null;
        AttributeRule? required = null;
        var hasValidationAttributes = false;
        var hasOwnValidationAttributes = false;
        foreach (AppliedAttribute applied in GetAppliedAttributes(member, context.Compilation))
        {
            bool isDisplay = SymbolEqualityComparer.Default.Equals(applied.Class, context.Known.DisplayAttribute);
            bool isValidation = KnownSymbols.InheritsFrom(applied.Class, context.Known.ValidationAttribute);
            if (!isDisplay && !isValidation)
                continue;
            hasValidationAttributes |= isValidation;
            hasOwnValidationAttributes |= isValidation && !applied.IsInherited;
            AttributeInfo? attribute = null;
            string error = "the attribute type is not accessible from this assembly";
            if (
                !context.Compilation.IsSymbolAccessibleWithin(applied.Class, context.Compilation.Assembly)
                || !applied.TryRead(out attribute, out error)
            )
            {
                context.Diagnostics.Add(
                    Diagnostic.Create(
                        Diagnostics.UnsupportedAttribute,
                        applied.Location ?? member.Locations.FirstOrDefault(),
                        applied.Class.Name,
                        name,
                        error
                    )
                );
                continue;
            }
            if (isDisplay)
            {
                display = attribute;
                continue;
            }

            PropertyRule? rule = CreateRule(context, name, propertyType, attribute);
            // Like Validator.TryValidateProperty: the first required attribute runs first and stops validation on failure
            if (
                rule is AttributeRule attributeRule
                && required is null
                && KnownSymbols.InheritsFrom(attribute.Class, context.Known.RequiredAttribute)
            )
            {
                required = attributeRule with { IsRequired = true };
            }
            else if (rule is not null)
            {
                rules.Add(rule);
            }
        }
        // The generated validation of the base type reads the property virtually, so it covers an override that only
        // inherits its attributes. A property hiding a validated one with 'new' needs its own (possibly empty) rules
        if (isValidatedByBase && member is IPropertySymbol { IsOverride: true } && !hasOwnValidationAttributes)
            return null;
        if (!hasValidationAttributes && !isValidatedByBase)
            return null;
        if (required is not null)
            rules.Insert(0, required);
        return new ValidatedProperty(name, display, rules.ToImmutableArray(), isValidatedByBase);
    }

    private static PropertyRule? CreateRule(
        BuildContext context,
        string propertyName,
        ITypeSymbol propertyType,
        AttributeInfo attribute
    )
    {
        bool isCustomValidation = SymbolEqualityComparer.Default.Equals(
            attribute.Class,
            context.Known.CustomValidationAttribute
        );
        bool isCompare = SymbolEqualityComparer.Default.Equals(attribute.Class, context.Known.CompareAttribute);
        if (!isCustomValidation && !isCompare)
            return new AttributeRule(
                attribute,
                IsRequired: false,
                IsTrimSafe: context.Known.IsTrimSafeFor(attribute.Class, propertyType)
            );

        if (
            attribute.HasNamedArgument("ErrorMessageResourceName")
            || attribute.HasNamedArgument("ErrorMessageResourceType")
        )
        {
            context.Diagnostics.Add(
                Diagnostic.Create(
                    Diagnostics.UnsupportedAttribute,
                    attribute.Location,
                    attribute.Class.Name,
                    propertyName,
                    "resource error messages are not supported, use ErrorMessage"
                )
            );
            return null;
        }
        return isCustomValidation
            ? CreateCustomValidationRule(context, propertyName, propertyType, attribute)
            : CreateCompareRule(context, propertyName, attribute);
    }

    private static CustomValidationRule? CreateCustomValidationRule(
        BuildContext context,
        string propertyName,
        ITypeSymbol propertyType,
        AttributeInfo attribute
    )
    {
        var validatorType = attribute.GetValue(0) as INamedTypeSymbol;
        string? methodName = attribute.GetString(1);
        // CustomValidationAttribute looks the method up with GetMethod(name, Public | Static), so require the same
        List<IMethodSymbol> candidates =
            validatorType is null || methodName is null
                ? []
                : validatorType
                    .GetMembers(methodName)
                    .OfType<IMethodSymbol>()
                    .Where(m =>
                        m
                            is {
                                MethodKind: MethodKind.Ordinary,
                                IsStatic: true,
                                DeclaredAccessibility: Accessibility.Public
                            }
                    )
                    .ToList();
        if (
            validatorType is null
            || candidates is not [{ IsGenericMethod: false, Parameters.Length: 1 or 2 } method]
            || !SymbolEqualityComparer.Default.Equals(method.ReturnType, context.Known.ValidationResult)
            || !context.Compilation.ClassifyCommonConversion(propertyType, method.Parameters[0].Type).IsImplicit
            || (
                method.Parameters.Length == 2
                && !SymbolEqualityComparer.Default.Equals(method.Parameters[1].Type, context.Known.ValidationContext)
            )
        )
        {
            context.Diagnostics.Add(
                Diagnostic.Create(
                    Diagnostics.InvalidCustomValidationMethod,
                    attribute.Location,
                    propertyName,
                    methodName ?? "?",
                    validatorType?.Name ?? "?"
                )
            );
            return null;
        }
        return new CustomValidationRule(
            $"{AttributeRenderer.FullName(validatorType)}.{AttributeRenderer.EscapeIdentifier(method.Name)}",
            method.Parameters.Length == 2,
            attribute.GetNamedString("ErrorMessage")
        );
    }

    private static CompareRule? CreateCompareRule(BuildContext context, string propertyName, AttributeInfo attribute)
    {
        string? otherProperty = attribute.GetString(0);
        ISymbol? other = otherProperty is null ? null : FindReadableProperty(context, otherProperty);
        if (otherProperty is null || other is null)
        {
            context.Diagnostics.Add(
                Diagnostic.Create(
                    Diagnostics.InvalidCompareTarget,
                    attribute.Location,
                    propertyName,
                    otherProperty ?? "?",
                    context.Type.Name
                )
            );
            return null;
        }
        AttributeInfo? otherDisplay = null;
        foreach (AppliedAttribute applied in GetAppliedAttributes(other, context.Compilation))
        {
            if (
                SymbolEqualityComparer.Default.Equals(applied.Class, context.Known.DisplayAttribute)
                && applied.TryRead(out AttributeInfo? display, out _)
            )
            {
                otherDisplay = display;
            }
        }
        string otherValue = other.IsStatic
            ? $"{AttributeRenderer.FullName(other.ContainingType)}.{AttributeRenderer.EscapeIdentifier(otherProperty)}"
            : $"this.{AttributeRenderer.EscapeIdentifier(otherProperty)}";
        return new CompareRule(otherProperty, otherValue, otherDisplay, attribute.GetNamedString("ErrorMessage"));
    }

    /// <summary> Whether a base type has validation for a property with this name </summary>
    private static bool IsValidatedByBase(BuildContext context, string name)
    {
        for (INamedTypeSymbol? current = context.Type.BaseType; current is not null; current = current.BaseType)
        {
            foreach (ISymbol member in current.GetMembers())
            {
                bool isSameProperty = member switch
                {
                    IPropertySymbol { IsStatic: false } property => property.Name == name,
                    IFieldSymbol { IsStatic: false } field => context.Known.IsObservableProperty(field)
                        && GetGeneratedPropertyName(field.Name) == name,
                    _ => false,
                };
                if (isSameProperty && HasValidationAttributes(member, context.Known, context.Compilation))
                    return true;
            }
        }
        return false;
    }

    /// <summary> Finds a readable property, or the [ObservableProperty] field it is generated from </summary>
    private static ISymbol? FindReadableProperty(BuildContext context, string name)
    {
        for (INamedTypeSymbol? current = context.Type; current is not null; current = current.BaseType)
        {
            foreach (ISymbol member in current.GetMembers())
            {
                if (
                    member
                        is IPropertySymbol
                        {
                            IsIndexer: false,
                            CanBeReferencedByName: true,
                            GetMethod: not null
                        } property
                    && property.Name == name
                )
                    return property;
                if (
                    member is IFieldSymbol field
                    && context.Known.IsObservableProperty(field)
                    && GetGeneratedPropertyName(field.Name) == name
                )
                {
                    return field;
                }
            }
        }
        return null;
    }

    /// <summary> Whether a property or [ObservableProperty] field has validation attributes, including <c>[property: ...]</c> ones </summary>
    public static bool HasValidationAttributes(ISymbol member, KnownSymbols known, Compilation compilation) =>
        GetAppliedAttributes(member, compilation)
            .Any(a => KnownSymbols.InheritsFrom(a.Class, known.ValidationAttribute));

    /// <summary>
    /// The attributes of a member, as <c>Validator.TryValidateProperty</c> sees them. An override also gets the
    /// attributes of the properties it overrides, unless it declares the same attribute type. An [ObservableProperty]
    /// field also gets its <c>[property: ...]</c> attributes, which the compiler ignores on fields and
    /// CommunityToolkit.Mvvm copies to the generated property.
    /// </summary>
    private static List<AppliedAttribute> GetAppliedAttributes(ISymbol member, Compilation compilation)
    {
        var attributes = new List<AppliedAttribute>();
        AddAttributes(attributes, member, isInherited: false, skip: []);
        for (
            IPropertySymbol? overridden = (member as IPropertySymbol)?.OverriddenProperty;
            overridden is not null;
            overridden = overridden.OverriddenProperty
        )
        {
            List<INamedTypeSymbol> declared = attributes.Select(a => a.Class).Where(c => !AllowsMultiple(c)).ToList();
            AddAttributes(attributes, overridden, isInherited: true, skip: declared);
        }
        if (member is not IFieldSymbol)
            return attributes;
        foreach (SyntaxReference reference in member.DeclaringSyntaxReferences)
        {
            if (reference.GetSyntax() is not VariableDeclaratorSyntax { Parent.Parent: FieldDeclarationSyntax field })
                continue;
#pragma warning disable RS1030 // The field can be declared in another partial part than the one being analyzed
            SemanticModel model = compilation.GetSemanticModel(field.SyntaxTree);
#pragma warning restore RS1030
            foreach (AttributeListSyntax list in field.AttributeLists)
            {
                if (!list.Target?.Identifier.IsKind(SyntaxKind.PropertyKeyword) ?? true)
                    continue;
                foreach (AttributeSyntax syntax in list.Attributes)
                {
                    if (AttributeRenderer.GetAttributeClass(syntax, model) is { } attributeClass)
                    {
                        attributes.Add(
                            new AppliedAttribute(
                                attributeClass,
                                syntax.GetLocation(),
                                false,
                                ([NotNullWhen(true)] out AttributeInfo? attribute, out string error) =>
                                    AttributeRenderer.TryCreate(syntax, attributeClass, model, out attribute, out error)
                            )
                        );
                    }
                }
            }
        }
        return attributes;
    }

    private static void AddAttributes(
        List<AppliedAttribute> attributes,
        ISymbol member,
        bool isInherited,
        List<INamedTypeSymbol> skip
    )
    {
        foreach (AttributeData data in member.GetAttributes())
        {
            if (
                data.AttributeClass is not { } attributeClass
                || skip.Contains(attributeClass, SymbolEqualityComparer.Default)
            )
                continue;
            attributes.Add(
                new AppliedAttribute(
                    attributeClass,
                    data.ApplicationSyntaxReference?.GetSyntax().GetLocation(),
                    isInherited,
                    ([NotNullWhen(true)] out AttributeInfo? attribute, out string error) =>
                        AttributeRenderer.TryCreate(data, out attribute, out error)
                )
            );
        }
    }

    /// <summary> Whether [AttributeUsage(AllowMultiple = true)] applies, like TypeDescriptor merging inherited attributes </summary>
    private static bool AllowsMultiple(INamedTypeSymbol attributeClass)
    {
        for (INamedTypeSymbol? current = attributeClass; current is not null; current = current.BaseType)
        {
            AttributeData? usage = current
                .GetAttributes()
                .FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == "System.AttributeUsageAttribute");
            if (usage is not null)
                return usage.NamedArguments.Any(a => a.Key == "AllowMultiple" && a.Value.Value is true);
        }
        return false;
    }

    private static bool IsPartialHierarchy(INamedTypeSymbol type)
    {
        for (INamedTypeSymbol? current = type; current is not null; current = current.ContainingType)
        {
            if (
                !current.DeclaringSyntaxReferences.All(r =>
                    r.GetSyntax() is TypeDeclarationSyntax declaration
                    && declaration.Modifiers.Any(SyntaxKind.PartialKeyword)
                )
            )
            {
                return false;
            }
        }
        return true;
    }

    private sealed class BuildContext(INamedTypeSymbol type, KnownSymbols known, Compilation compilation)
    {
        public INamedTypeSymbol Type { get; } = type;
        public KnownSymbols Known { get; } = known;
        public Compilation Compilation { get; } = compilation;
        public ImmutableArray<Diagnostic>.Builder Diagnostics { get; } = ImmutableArray.CreateBuilder<Diagnostic>();
    }

    private delegate bool AttributeReader([NotNullWhen(true)] out AttributeInfo? attribute, out string error);

    /// <summary> An attribute read from a symbol, or from <c>[property: ...]</c> syntax on a field </summary>
    private sealed class AppliedAttribute(
        INamedTypeSymbol attributeClass,
        Location? location,
        bool isInherited,
        AttributeReader read
    )
    {
        private readonly AttributeReader _read = read;

        public INamedTypeSymbol Class { get; } = attributeClass;

        public Location? Location { get; } = location;

        /// <summary> Whether the attribute comes from a property this one overrides </summary>
        public bool IsInherited { get; } = isInherited;

        public bool TryRead([NotNullWhen(true)] out AttributeInfo? attribute, out string error) =>
            _read(out attribute, out error);
    }
}
