using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Darp.MvvmValidator.Generator;

/// <summary> The validation of a type deriving from ValidatingObservableObject </summary>
/// <param name="Type"> The type </param>
/// <param name="CanGenerate"> Whether the type and all containing types are partial and not file-local </param>
/// <param name="Properties"> The properties declared in this type that have validation attributes </param>
/// <param name="Diagnostics"> Everything that prevents generating the validation </param>
internal sealed record TypeValidation(
    INamedTypeSymbol Type,
    bool CanGenerate,
    ImmutableArray<ValidatedProperty> Properties,
    ImmutableArray<Diagnostic> Diagnostics
);

/// <summary> A property with validation attributes </summary>
/// <param name="Name"> The property name. For an [ObservableProperty] field, the name of the generated property </param>
/// <param name="TypeName"> The fully qualified property type, including its nullable annotation </param>
/// <param name="Display"> The [Display] attribute, if any </param>
/// <param name="Rules"> The rules in execution order. A required rule always comes first </param>
/// <param name="IsValidatedByBase">
/// Whether a base type validates a property with this name, so its ValidateAllProperties already covers it
/// </param>
/// <param name="TrimWarningTarget">
/// The documentation ID of the property, when the trimmer's IL2026 for its attributes can be suppressed: the generated
/// code avoids the reflection of every attribute constructor that requires unreferenced code, and nobody wrote the
/// accessors, which the suppression also covers
/// </param>
internal sealed record ValidatedProperty(
    string Name,
    string TypeName,
    AttributeInfo? Display,
    ImmutableArray<PropertyRule> Rules,
    bool IsValidatedByBase,
    string? TrimWarningTarget
);

internal abstract record PropertyRule;

/// <summary> Runs a static instance of the validation attribute </summary>
/// <param name="Attribute"> The attribute </param>
/// <param name="IsRequired"> Whether a failure stops validating the property </param>
/// <param name="SuppressTrimWarning">
/// Whether the IL2026 of the attribute constructor can be suppressed, because this property never reaches the
/// reflection it warns about
/// </param>
internal sealed record AttributeRule(AttributeInfo Attribute, bool IsRequired, bool SuppressTrimWarning) : PropertyRule;

/// <summary> Calls the method of a [CustomValidation] directly instead of looking it up with reflection </summary>
/// <param name="Method"> The fully qualified method </param>
/// <param name="PassesContext"> Whether the method takes a ValidationContext </param>
/// <param name="ErrorMessage"> A C# expression for the message format of the attribute, possibly the literal <c>null</c> </param>
internal sealed record CustomValidationRule(string Method, bool PassesContext, string ErrorMessage) : PropertyRule;

/// <summary> Implements [Compare] by reading the other property directly instead of with reflection </summary>
/// <param name="OtherProperty"> The name of the compared property </param>
/// <param name="OtherValue"> The C# expression reading the compared property, which can be static </param>
/// <param name="OtherDisplay"> The [Display] attribute of the compared property, if any </param>
/// <param name="ErrorMessage"> A C# expression for the message format of the attribute, possibly the literal <c>null</c> </param>
internal sealed record CompareRule(
    string OtherProperty,
    string OtherValue,
    AttributeInfo? OtherDisplay,
    string ErrorMessage
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
            if (
                TryGetProperty(member, known, out string? name, out ITypeSymbol? propertyType)
                && IsReadable(member)
                && CreateProperty(context, name, propertyType, member) is { } property
            )
            {
                properties.Add(property);
            }
        }

        bool canGenerate = CanExtend(type);
        if (!canGenerate && properties.Count > 0)
        {
            context.Diagnostics.Add(
                Diagnostic.Create(Diagnostics.TypeMustBePartial, type.Locations.FirstOrDefault(), type.Name)
            );
        }
        return new TypeValidation(type, canGenerate, properties.ToImmutable(), context.Diagnostics.ToImmutable());
    }

    /// <summary> Whether a property or [ObservableProperty] field has validation attributes, including <c>[property: ...]</c> ones </summary>
    public static bool HasValidationAttributes(ISymbol member, KnownSymbols known, Compilation compilation) =>
        GetAppliedAttributes(member, compilation)
            .Any(a => KnownSymbols.InheritsFrom(a.Class, known.ValidationAttribute));

    /// <summary>
    /// The instance property a member stands for: the property itself, or the property CommunityToolkit.Mvvm
    /// generates for an [ObservableProperty] field. Explicit interface implementations cannot be referenced by name
    /// </summary>
    private static bool TryGetProperty(
        ISymbol member,
        KnownSymbols known,
        [NotNullWhen(true)] out string? name,
        [NotNullWhen(true)] out ITypeSymbol? type
    )
    {
        switch (member)
        {
            case IPropertySymbol { IsStatic: false, IsIndexer: false, CanBeReferencedByName: true } property:
                (name, type) = (property.Name, property.Type);
                return true;
            case IFieldSymbol { IsStatic: false } field when known.IsObservableProperty(field):
                (name, type) = (GetGeneratedPropertyName(field.Name), field.Type);
                return true;
            default:
                (name, type) = (null, null);
                return false;
        }
    }

    /// <summary>
    /// Whether nobody wrote the property's accessors: CommunityToolkit.Mvvm generates them for [ObservableProperty], and
    /// auto-properties have none. A suppression for a property also covers its accessors, so it must not hide warnings
    /// in accessor code someone wrote
    /// </summary>
    private static bool HasNoHandWrittenAccessors(ISymbol member) =>
        member is IFieldSymbol
        || member.DeclaringSyntaxReferences.All(r =>
            r.GetSyntax() is PropertyDeclarationSyntax { ExpressionBody: null, AccessorList: { } accessors }
            && accessors.Accessors.All(a => a is { Body: null, ExpressionBody: null })
        );

    /// <summary>
    /// The documentation ID of a property, also for one CommunityToolkit.Mvvm generates and this generator cannot see.
    /// The trimmer expects it without the <c>~</c> prefix that SuppressMessage uses
    /// </summary>
    private static string GetDocumentationId(INamedTypeSymbol type, string propertyName) =>
        $"P:{type.GetDocumentationCommentId()?.Substring(2)}.{propertyName}";

    /// <summary> Mirrors how CommunityToolkit.Mvvm names the property generated for an [ObservableProperty] field </summary>
    private static string GetGeneratedPropertyName(string fieldName)
    {
        string name = fieldName;
        if (name.StartsWith("m_", StringComparison.Ordinal))
            name = name.Substring(2);
        else if (name.StartsWith("_", StringComparison.Ordinal))
            name = name.TrimStart('_');
        return name.Length == 0 ? fieldName : char.ToUpper(name[0], CultureInfo.InvariantCulture) + name.Substring(1);
    }

    /// <summary> The property generated for an [ObservableProperty] field always has a public getter </summary>
    private static bool IsReadable(ISymbol member) => member is IFieldSymbol or IPropertySymbol { GetMethod: not null };

    /// <summary> Generated code passes values as object and calls validation methods statically </summary>
    private static bool IsSupportedType(ITypeSymbol type) =>
        type
            is not (
                { IsRefLikeType: true }
                or ITypeParameterSymbol { AllowsRefLikeType: true }
                or { TypeKind: TypeKind.Pointer or TypeKind.FunctionPointer or TypeKind.Dynamic }
            );

    private static ValidatedProperty? CreateProperty(
        BuildContext context,
        string name,
        ITypeSymbol propertyType,
        ISymbol member
    )
    {
        bool isValidatedByBase = IsValidatedByBase(context, name);
        Location? memberLocation = member.Locations.FirstOrDefault();
        var rules = new List<PropertyRule>();
        AttributeInfo? display = null;
        AttributeRule? required = null;
        var hasValidationAttributes = false;
        var hasOwnAttributes = false;
        var hasSuppressibleTrimWarnings = false;
        var hasUnsafeTrimWarnings = false;
        foreach (AppliedAttribute applied in GetAppliedAttributes(member, context.Compilation))
        {
            bool isDisplay = SymbolEqualityComparer.Default.Equals(applied.Class, context.Known.DisplayAttribute);
            bool isValidation = KnownSymbols.InheritsFrom(applied.Class, context.Known.ValidationAttribute);
            // The trimmer reports attributes where they are declared, so inherited ones are the base type's concern.
            // Any other attribute that requires unreferenced code keeps the warnings of the whole property
            if (
                !applied.IsInherited
                && applied.Constructor is { } constructor
                && context.Known.RequiresUnreferencedCode(constructor)
            )
            {
                if ((isDisplay || isValidation) && context.Known.CanSuppressTrimWarning(applied.Class, propertyType))
                    hasSuppressibleTrimWarnings = true;
                else
                    hasUnsafeTrimWarnings = true;
            }
            if (!isDisplay && !isValidation)
                continue;
            hasValidationAttributes |= isValidation;
            hasOwnAttributes |= !applied.IsInherited;
            Location? location = applied.Location ?? memberLocation;
            if (!applied.TryRead(out AttributeInfo? attribute, out string error))
            {
                context.Report(Diagnostics.UnsupportedValidation, location, name, $"[{applied.Class.Name}] {error}");
                continue;
            }
            if (!attribute.ReferencedSymbols.All(s => context.Compilation.IsSymbolAccessibleWithin(s, context.Type)))
            {
                context.Report(
                    Diagnostics.UnsupportedValidation,
                    location,
                    name,
                    $"[{applied.Class.Name}] uses a type or member that is not accessible from '{context.Type.Name}'"
                );
                continue;
            }
            if (isDisplay)
            {
                display = attribute;
                continue;
            }

            PropertyRule? rule = CreateRule(context, name, propertyType, attribute, location);
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
        if (isValidatedByBase && member is IPropertySymbol { IsOverride: true } && !hasOwnAttributes)
            return null;
        if (!hasValidationAttributes && !isValidatedByBase)
            return null;
        if (!IsSupportedType(propertyType))
        {
            context.Report(
                Diagnostics.UnsupportedValidation,
                memberLocation,
                name,
                $"its type '{propertyType.ToDisplayString()}' is a ref struct, a pointer or dynamic"
            );
            return null;
        }
        if (required is not null)
            rules.Insert(0, required);
        return new ValidatedProperty(
            name,
            AttributeRenderer.FullNameWithNullability(propertyType),
            display,
            rules.ToImmutableArray(),
            isValidatedByBase,
            hasSuppressibleTrimWarnings && !hasUnsafeTrimWarnings && HasNoHandWrittenAccessors(member)
                ? GetDocumentationId(context.Type, name)
                : null
        );
    }

    private static PropertyRule? CreateRule(
        BuildContext context,
        string propertyName,
        ITypeSymbol propertyType,
        AttributeInfo attribute,
        Location? location
    )
    {
        if (SymbolEqualityComparer.Default.Equals(attribute.Class, context.Known.CustomValidationAttribute))
            return CreateCustomValidationRule(context, propertyName, propertyType, attribute, location);
        if (SymbolEqualityComparer.Default.Equals(attribute.Class, context.Known.CompareAttribute))
            return CreateCompareRule(context, propertyName, attribute, location);
        return new AttributeRule(
            attribute,
            IsRequired: false,
            SuppressTrimWarning: context.Known.CanSuppressTrimWarning(attribute.Class, propertyType)
        );
    }

    /// <summary>
    /// The message format of an attribute the generated code implements itself, as a C# expression. Resource arguments
    /// go to ValidationAttribute unchanged, which checks them at runtime like for every other attribute
    /// </summary>
    private static string GetErrorMessage(AttributeInfo attribute)
    {
        string? message = attribute.GetNamedString("ErrorMessage");
        var resourceType =
            attribute.NamedArguments.FirstOrDefault(a => a.Key == "ErrorMessageResourceType").Value?.Value
            as INamedTypeSymbol;
        string? resourceName = attribute.GetNamedString("ErrorMessageResourceName");
        if (resourceType is null && resourceName is null)
            return AttributeRenderer.StringLiteral(message);
        string type = resourceType is null ? "null" : $"typeof({AttributeRenderer.FullName(resourceType)})";
        return $"GetErrorMessage({AttributeRenderer.StringLiteral(message)}, {type}, {AttributeRenderer.StringLiteral(resourceName)})";
    }

    private static CustomValidationRule? CreateCustomValidationRule(
        BuildContext context,
        string propertyName,
        ITypeSymbol propertyType,
        AttributeInfo attribute,
        Location? location
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
            // The generated call passes arguments by value
            || !method.Parameters.All(p => p.RefKind is RefKind.None or RefKind.In)
        )
        {
            context.Report(
                Diagnostics.InvalidCustomValidationMethod,
                location,
                propertyName,
                methodName ?? "?",
                validatorType?.Name ?? "?"
            );
            return null;
        }
        return new CustomValidationRule(
            $"{AttributeRenderer.FullName(validatorType)}.{AttributeRenderer.EscapeIdentifier(method.Name)}",
            method.Parameters.Length == 2,
            GetErrorMessage(attribute)
        );
    }

    private static CompareRule? CreateCompareRule(
        BuildContext context,
        string propertyName,
        AttributeInfo attribute,
        Location? location
    )
    {
        string? otherProperty = attribute.GetString(0);
        ISymbol? other = otherProperty is null ? null : FindComparedProperty(context, otherProperty);
        if (otherProperty is null || other is null)
        {
            context.Report(
                Diagnostics.InvalidCompareTarget,
                location,
                propertyName,
                otherProperty ?? "?",
                context.Type.Name
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
        return new CompareRule(otherProperty, otherValue, otherDisplay, GetErrorMessage(attribute));
    }

    /// <summary>
    /// Finds the property <c>{name}</c> means in the view model: the nearest declaration wins, so a hiding member that
    /// is not a readable, accessible property makes the target invalid
    /// </summary>
    private static ISymbol? FindComparedProperty(BuildContext context, string name)
    {
        for (INamedTypeSymbol? current = context.Type; current is not null; current = current.BaseType)
        {
            ISymbol? member =
                current.GetMembers(name).FirstOrDefault()
                ?? current
                    .GetMembers()
                    .FirstOrDefault(m =>
                        m is IFieldSymbol && TryGetProperty(m, context.Known, out string? n, out _) && n == name
                    );
            if (member is null)
                continue;
            // Like CompareAttribute, which looks the property up with reflection, static properties are allowed
            ITypeSymbol? type = member switch
            {
                IPropertySymbol { IsIndexer: false, CanBeReferencedByName: true } property => property.Type,
                IFieldSymbol field when TryGetProperty(field, context.Known, out _, out ITypeSymbol? fieldType) =>
                    fieldType,
                _ => null,
            };
            bool isValid =
                type is not null
                && IsReadable(member)
                && IsSupportedType(type)
                && (
                    member is not IPropertySymbol { GetMethod: { } getter }
                    || context.Compilation.IsSymbolAccessibleWithin(getter, context.Type, context.Type)
                );
            return isValid ? member : null;
        }
        return null;
    }

    /// <summary> Whether a base type has generated validation for a property with this name </summary>
    private static bool IsValidatedByBase(BuildContext context, string name)
    {
        for (INamedTypeSymbol? current = context.Type.BaseType; current is not null; current = current.BaseType)
        {
            foreach (ISymbol member in current.GetMembers())
            {
                if (
                    TryGetProperty(member, context.Known, out string? memberName, out _)
                    && memberName == name
                    && IsReadable(member)
                    && HasValidationAttributes(member, context.Known, context.Compilation)
                )
                {
                    return true;
                }
            }
        }
        return false;
    }

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
            List<INamedTypeSymbol> declared = attributes.Select(a => a.Class).ToList();
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
                                model.GetSymbolInfo(syntax).Symbol as IMethodSymbol,
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
                    data.AttributeConstructor,
                    ([NotNullWhen(true)] out AttributeInfo? attribute, out string error) =>
                        AttributeRenderer.TryCreate(data, out attribute, out error)
                )
            );
        }
    }

    /// <summary> Generated code adds a partial declaration in another file, which file-local types do not allow </summary>
    private static bool CanExtend(INamedTypeSymbol type)
    {
        for (INamedTypeSymbol? current = type; current is not null; current = current.ContainingType)
        {
            if (
                current.IsFileLocal
                || !current.DeclaringSyntaxReferences.All(r =>
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

        public void Report(DiagnosticDescriptor descriptor, Location? location, params object[] arguments) =>
            Diagnostics.Add(Diagnostic.Create(descriptor, location, arguments));
    }

    private delegate bool AttributeReader([NotNullWhen(true)] out AttributeInfo? attribute, out string error);

    /// <summary> An attribute read from a symbol, or from <c>[property: ...]</c> syntax on a field </summary>
    private sealed class AppliedAttribute(
        INamedTypeSymbol attributeClass,
        Location? location,
        bool isInherited,
        IMethodSymbol? constructor,
        AttributeReader read
    )
    {
        private readonly AttributeReader _read = read;

        public IMethodSymbol? Constructor { get; } = constructor;

        public INamedTypeSymbol Class { get; } = attributeClass;

        public Location? Location { get; } = location;

        /// <summary> Whether the attribute comes from a property this one overrides </summary>
        public bool IsInherited { get; } = isInherited;

        public bool TryRead([NotNullWhen(true)] out AttributeInfo? attribute, out string error) =>
            _read(out attribute, out error);
    }
}
