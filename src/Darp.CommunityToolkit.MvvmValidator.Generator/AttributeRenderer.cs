using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace Darp.MvvmValidator.Generator;

/// <summary> An attribute argument as C# code, plus its constant value (a <see cref="ITypeSymbol"/> for typeof) </summary>
/// <param name="Expression"> The C# expression </param>
/// <param name="Value"> The constant value, if any </param>
/// <param name="Type"> The type of <paramref name="Expression"/>, if known </param>
/// <param name="References"> Every type <paramref name="Expression"/> names </param>
internal sealed record AttributeArgument(
    string Expression,
    object? Value,
    ITypeSymbol? Type,
    ImmutableArray<ITypeSymbol> References
)
{
    public static AttributeArgument Create(
        string expression,
        object? value,
        ITypeSymbol? type,
        params ITypeSymbol?[] references
    ) => new(expression, value, type, references.OfType<ITypeSymbol>().ToImmutableArray());
}

/// <summary> An applied attribute that can be turned back into C# code creating an equal instance </summary>
internal sealed record AttributeInfo(
    INamedTypeSymbol Class,
    IMethodSymbol Constructor,
    ImmutableArray<AttributeArgument> Arguments,
    ImmutableArray<KeyValuePair<string, AttributeArgument>> NamedArguments
)
{
    /// <summary> <c>new Attribute(args) { Named = value }</c> </summary>
    public string CreationExpression
    {
        get
        {
            var builder = new StringBuilder("new ")
                .Append(AttributeRenderer.FullName(Class))
                .Append('(')
                .Append(string.Join(", ", Arguments.Select(a => a.Expression)))
                .Append(')');
            if (NamedArguments.Length > 0)
            {
                builder
                    .Append(" { ")
                    .Append(
                        string.Join(
                            ", ",
                            NamedArguments.Select(a =>
                                $"{AttributeRenderer.EscapeIdentifier(a.Key)} = {a.Value.Expression}"
                            )
                        )
                    )
                    .Append(" }");
            }
            return builder.ToString();
        }
    }

    /// <summary> The symbols <see cref="CreationExpression"/> uses, which must be accessible from the generated code </summary>
    public IEnumerable<ISymbol> ReferencedSymbols =>
        new ISymbol[] { Class, Constructor }
            .Concat(Arguments.SelectMany(a => a.References))
            .Concat(NamedArguments.SelectMany(a => a.Value.References));

    public string? GetString(int index) => index < Arguments.Length ? Arguments[index].Value as string : null;

    public object? GetValue(int index) => index < Arguments.Length ? Arguments[index].Value : null;

    public string? GetNamedString(string name) =>
        NamedArguments.FirstOrDefault(a => a.Key == name).Value?.Value as string;
}

internal static class AttributeRenderer
{
    public static string FullName(ITypeSymbol type) => type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

    /// <summary> The fully qualified type as it is declared, e.g. <c>string?</c> for a nullable reference type </summary>
    public static string FullNameWithNullability(ITypeSymbol type) => type.ToDisplayString(s_nullableFormat);

    private static readonly SymbolDisplayFormat s_nullableFormat =
        SymbolDisplayFormat.FullyQualifiedFormat.AddMiscellaneousOptions(
            SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier
        );

    /// <summary> A namespace or type parameter name that is valid C#, e.g. with keywords escaped </summary>
    public static string DisplayName(ISymbol symbol) => symbol.ToDisplayString(s_escapedFormat);

    private static readonly SymbolDisplayFormat s_escapedFormat = new(
        globalNamespaceStyle: SymbolDisplayGlobalNamespaceStyle.Omitted,
        typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
        miscellaneousOptions: SymbolDisplayMiscellaneousOptions.EscapeKeywordIdentifiers
    );

    public static string EscapeIdentifier(string name) =>
        SyntaxFacts.GetKeywordKind(name) == SyntaxKind.None ? name : "@" + name;

    public static string StringLiteral(string? value) =>
        value is null ? "null" : SymbolDisplay.FormatLiteral(value, quote: true);

    /// <summary> Reads an attribute applied to a symbol </summary>
    public static bool TryCreate(AttributeData attribute, [NotNullWhen(true)] out AttributeInfo? info, out string error)
    {
        info = null;
        if (attribute.AttributeClass is null || attribute.AttributeConstructor is null)
        {
            error = "the attribute does not bind";
            return false;
        }
        var arguments = ImmutableArray.CreateBuilder<AttributeArgument>();
        for (var i = 0; i < attribute.ConstructorArguments.Length; i++)
        {
            TypedConstant constant = attribute.ConstructorArguments[i];
            AttributeArgument? argument =
                constant.Kind == TypedConstantKind.Error
                    ? FromNameof(GetPositionalSyntax(attribute, i))
                    : FromTypedConstant(constant);
            if (argument is null)
            {
                error = $"constructor argument {i + 1} is not a constant";
                return false;
            }
            // The cast keeps overload resolution on the constructor the attribute was applied with
            arguments.Add(Cast(argument, attribute.AttributeConstructor.Parameters[i].Type));
        }
        var namedArguments = ImmutableArray.CreateBuilder<KeyValuePair<string, AttributeArgument>>();
        foreach (KeyValuePair<string, TypedConstant> named in attribute.NamedArguments)
        {
            AttributeArgument? argument =
                named.Value.Kind == TypedConstantKind.Error
                    ? FromNameof(GetNamedSyntax(attribute, named.Key))
                    : FromTypedConstant(named.Value);
            if (argument is null)
            {
                error = $"argument '{named.Key}' is not a constant";
                return false;
            }
            namedArguments.Add(new(named.Key, argument));
        }
        info = new AttributeInfo(
            attribute.AttributeClass,
            attribute.AttributeConstructor,
            arguments.ToImmutable(),
            namedArguments.ToImmutable()
        );
        error = "";
        return true;
    }

    /// <summary> Gets the class of an attribute that only exists as syntax, e.g. <c>[property: Required]</c> on a field </summary>
    public static INamedTypeSymbol? GetAttributeClass(AttributeSyntax syntax, SemanticModel model) =>
        model.GetSymbolInfo(syntax).Symbol is IMethodSymbol { MethodKind: MethodKind.Constructor } constructor
            ? constructor.ContainingType
            : null;

    /// <summary> Reads an attribute that only exists as syntax, e.g. <c>[property: Required]</c> on a field </summary>
    public static bool TryCreate(
        AttributeSyntax syntax,
        INamedTypeSymbol attributeClass,
        SemanticModel model,
        [NotNullWhen(true)] out AttributeInfo? info,
        out string error
    )
    {
        info = null;
        if (
            model.GetOperation(syntax)
            is not IAttributeOperation { Operation: IObjectCreationOperation { Constructor: { } constructor } creation }
        )
        {
            error = "the attribute does not bind";
            return false;
        }

        // Arguments are in evaluation order, which differs from parameter order for named arguments
        var arguments = new AttributeArgument[constructor.Parameters.Length];
        foreach (IArgumentOperation argument in creation.Arguments)
        {
            if (argument.Parameter is not { } parameter || FromOperation(argument.Value) is not { } value)
            {
                error = $"argument '{argument.Syntax}' is not a constant";
                return false;
            }
            arguments[parameter.Ordinal] = Cast(value, parameter.Type);
        }
        if (arguments.Any(a => a is null))
        {
            error = "not all constructor arguments are known";
            return false;
        }

        var namedArguments = ImmutableArray.CreateBuilder<KeyValuePair<string, AttributeArgument>>();
        foreach (IOperation initializer in creation.Initializer?.Initializers ?? [])
        {
            if (
                initializer is not ISimpleAssignmentOperation { Target: IMemberReferenceOperation target } assignment
                || FromOperation(assignment.Value) is not { } value
            )
            {
                error = $"argument '{initializer.Syntax}' is not a constant";
                return false;
            }
            namedArguments.Add(new(target.Member.Name, value));
        }

        info = new AttributeInfo(
            attributeClass,
            constructor,
            arguments.ToImmutableArray(),
            namedArguments.ToImmutable()
        );
        error = "";
        return true;
    }

    private static AttributeArgument Cast(AttributeArgument argument, ITypeSymbol type) =>
        SymbolEqualityComparer.Default.Equals(argument.Type, type)
            ? argument
            : argument with
            {
                Expression = $"(({FullName(type)})({argument.Expression}))",
                Type = type,
                References = argument.References.Add(type),
            };

    private static AttributeArgument? FromOperation(IOperation operation)
    {
        if (operation.ConstantValue.HasValue)
        {
            return AttributeArgument.Create(
                RenderConstant(operation.ConstantValue.Value, operation.Type),
                operation.ConstantValue.Value,
                operation.Type,
                operation.Type
            );
        }
        switch (operation)
        {
            case IConversionOperation { Type: { } type } conversion:
                return FromOperation(conversion.Operand) is { } operand ? Cast(operand, type) : null;
            case ITypeOfOperation typeOf:
                return AttributeArgument.Create(
                    $"typeof({FullName(typeOf.TypeOperand)})",
                    typeOf.TypeOperand,
                    null,
                    typeOf.TypeOperand
                );
            case IArrayCreationOperation { Initializer: { } initializer, Type: IArrayTypeSymbol arrayType }:
                return FromElements(arrayType, initializer.ElementValues.Select(FromOperation));
            case ICollectionExpressionOperation { Type: IArrayTypeSymbol arrayType } collection:
                return FromElements(arrayType, collection.Elements.Select(FromOperation));
            default:
                return FromNameof(operation.Syntax as ExpressionSyntax);
        }
    }

    private static AttributeArgument? FromElements(IArrayTypeSymbol arrayType, IEnumerable<AttributeArgument?> elements)
    {
        var values = new List<AttributeArgument>();
        foreach (AttributeArgument? element in elements)
        {
            if (element is null)
                return null;
            values.Add(element);
        }
        return new AttributeArgument(
            $"new {FullName(arrayType)} {{ {string.Join(", ", values.Select(v => v.Expression))} }}",
            null,
            arrayType,
            values.SelectMany(v => v.References).Prepend(arrayType).ToImmutableArray()
        );
    }

    private static AttributeArgument? FromTypedConstant(TypedConstant constant)
    {
        switch (constant.Kind)
        {
            case TypedConstantKind.Array:
                return constant.IsNull || constant.Type is not IArrayTypeSymbol arrayType
                    ? AttributeArgument.Create("null", null, null)
                    : FromElements(arrayType, constant.Values.Select(FromTypedConstant));
            case TypedConstantKind.Type:
                return constant.Value is ITypeSymbol type
                    ? AttributeArgument.Create($"typeof({FullName(type)})", type, null, type)
                    : AttributeArgument.Create("null", null, null);
            case TypedConstantKind.Enum:
            case TypedConstantKind.Primitive:
                return AttributeArgument.Create(
                    RenderConstant(constant.Value, constant.Type),
                    constant.Value,
                    constant.Type,
                    constant.Type
                );
            default:
                return null;
        }
    }

    /// <summary> Properties added by other generators (e.g. [ObservableProperty]) are invisible here, so nameof() of them does not bind </summary>
    private static AttributeArgument? FromNameof(ExpressionSyntax? expression)
    {
        if (
            expression
            is not InvocationExpressionSyntax
            {
                Expression: IdentifierNameSyntax { Identifier.ValueText: "nameof" },
                ArgumentList.Arguments: [{ Expression: var target }],
            }
        )
        {
            return null;
        }
        string? name = target switch
        {
            IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
            MemberAccessExpressionSyntax memberAccess => memberAccess.Name.Identifier.ValueText,
            _ => null,
        };
        return name is null ? null : AttributeArgument.Create(StringLiteral(name), name, null);
    }

    private static ExpressionSyntax? GetPositionalSyntax(AttributeData attribute, int index)
    {
        if (attribute.ApplicationSyntaxReference?.GetSyntax() is not AttributeSyntax { ArgumentList: { } list })
            return null;
        var positional = list.Arguments.Where(a => a.NameEquals is null && a.NameColon is null).ToList();
        return index < positional.Count ? positional[index].Expression : null;
    }

    private static ExpressionSyntax? GetNamedSyntax(AttributeData attribute, string name) =>
        attribute.ApplicationSyntaxReference?.GetSyntax() is AttributeSyntax { ArgumentList: { } list }
            ? list.Arguments.FirstOrDefault(a => a.NameEquals?.Name.Identifier.ValueText == name)?.Expression
            : null;

    private static string RenderConstant(object? value, ITypeSymbol? type) =>
        value switch
        {
            null => type is null ? "null" : $"(({FullName(type)})null)",
            string or bool => RenderPrimitive(value),
            _ when type is null => RenderPrimitive(value),
            _ => $"(({FullName(type)})({RenderPrimitive(value)}))",
        };

    private static string RenderPrimitive(object value) =>
        value switch
        {
            string text => StringLiteral(text),
            char character => SymbolDisplay.FormatLiteral(character, quote: true),
            bool boolean => boolean ? "true" : "false",
            double d when double.IsNaN(d) => "double.NaN",
            double d when double.IsPositiveInfinity(d) => "double.PositiveInfinity",
            double d when double.IsNegativeInfinity(d) => "double.NegativeInfinity",
            double d => d.ToString("R", CultureInfo.InvariantCulture) + "d",
            float f when float.IsNaN(f) => "float.NaN",
            float f when float.IsPositiveInfinity(f) => "float.PositiveInfinity",
            float f when float.IsNegativeInfinity(f) => "float.NegativeInfinity",
            float f => f.ToString("R", CultureInfo.InvariantCulture) + "f",
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString() ?? "null",
        };
}
