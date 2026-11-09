using System.Collections;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Runtime.CompilerServices;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Darp.MvvmValidator;

/// <summary>
/// An <see cref="ObservableObject"/> that implements <see cref="INotifyDataErrorInfo"/> with source generated validation.
/// </summary>
/// <remarks>
/// Deriving types must be <see langword="partial"/>. Every property with validation attributes is validated whenever
/// it raises <see cref="INotifyPropertyChanged.PropertyChanged"/>, before the event reaches subscribers. This covers
/// <c>[ObservableProperty]</c> fields, <c>[ObservableProperty]</c> partial properties and properties that call
/// <c>SetProperty</c>. The generated code reads the property directly and does not use reflection.
/// </remarks>
public abstract class ValidatingObservableObject : ObservableObject, INotifyDataErrorInfo
{
    private static readonly PropertyChangedEventArgs s_hasErrorsChangedEventArgs = new(nameof(HasErrors));

    private readonly IServiceProvider? _serviceProvider;
    private readonly Dictionary<object, object?>? _items;
    private readonly Dictionary<string, ValidationResult[]> _errors = new(StringComparer.Ordinal);
    private readonly HashSet<string> _validatedProperties = new(StringComparer.Ordinal);

    /// <summary> Initializes a new instance </summary>
    protected ValidatingObservableObject() { }

    /// <summary> Initializes a new instance that passes services and items to every <see cref="ValidationContext"/> </summary>
    /// <param name="serviceProvider"> The service provider to make available to validation attributes </param>
    /// <param name="items"> The items to make available to validation attributes </param>
    protected ValidatingObservableObject(IServiceProvider? serviceProvider, IDictionary<object, object?>? items)
    {
        _serviceProvider = serviceProvider;
        _items = items is null ? null : new Dictionary<object, object?>(items);
    }

    /// <inheritdoc />
    public event EventHandler<DataErrorsChangedEventArgs>? ErrorsChanged;

    /// <inheritdoc />
    [Display(AutoGenerateField = false)]
    public bool HasErrors => _errors.Count > 0;

    /// <summary> Gets the validation errors of a property, or of all properties </summary>
    /// <param name="propertyName"> The property name, or <see langword="null"/> or empty for all properties </param>
    /// <returns> The validation errors </returns>
    public IEnumerable<ValidationResult> GetErrors(string? propertyName = null)
    {
        if (string.IsNullOrEmpty(propertyName))
            return _errors.Values.SelectMany(static errors => errors).ToArray();
        return _errors.TryGetValue(propertyName, out ValidationResult[]? errors) ? errors : [];
    }

    IEnumerable INotifyDataErrorInfo.GetErrors(string? propertyName) => GetErrors(propertyName);

    /// <summary> Validates the current value of a property </summary>
    /// <param name="propertyName"> The name of a property with validation attributes </param>
    /// <exception cref="ArgumentException"> The property has no validation attributes </exception>
    protected void ValidateProperty([CallerMemberName] string propertyName = null!)
    {
        if (!TryValidateProperty(propertyName))
        {
            throw new ArgumentException(
                $"'{GetType()}' has no generated validation for property '{propertyName}'. Make sure the property has validation attributes and the type and all containing types are partial.",
                nameof(propertyName)
            );
        }
    }

    /// <summary> Validates the current value of every property with validation attributes </summary>
    protected void ValidateAllProperties() => ValidateAllPropertiesCore();

    /// <summary> Removes the errors of a property, or of all properties </summary>
    /// <param name="propertyName"> The property name, or <see langword="null"/> for all properties </param>
    protected void ClearErrors(string? propertyName = null)
    {
        if (propertyName is not null)
        {
            SetErrors(propertyName, []);
            return;
        }
        foreach (string name in _errors.Keys.ToArray())
            SetErrors(name, []);
    }

    /// <summary> Replaces the errors of a property, for example with results from asynchronous or server-side validation </summary>
    /// <param name="propertyName"> The property name </param>
    /// <param name="errors"> The new errors. They are replaced the next time the property is validated </param>
    protected void SetErrors(string propertyName, IEnumerable<ValidationResult> errors)
    {
        ArgumentNullException.ThrowIfNull(propertyName);
        ArgumentNullException.ThrowIfNull(errors);
        ValidationResult[] newErrors = errors.ToArray();
        ValidationResult[] oldErrors = _errors.TryGetValue(propertyName, out ValidationResult[]? existing)
            ? existing
            : [];
        bool hadErrors = HasErrors;
        if (newErrors.Length == 0)
            _errors.Remove(propertyName);
        else
            _errors[propertyName] = newErrors;
        if (HaveSameMessages(oldErrors, newErrors))
            return;

        ErrorsChanged?.Invoke(this, new DataErrorsChangedEventArgs(propertyName));
        if (hadErrors != HasErrors)
            OnPropertyChanged(s_hasErrorsChangedEventArgs);
    }

    /// <summary> Validates a property before notifying subscribers, so they see up-to-date errors </summary>
    /// <param name="e"> The event args </param>
    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        if (!string.IsNullOrEmpty(e.PropertyName))
        {
            TryValidateProperty(e.PropertyName);
            ValidateDependentPropertiesCore(e.PropertyName);
        }
        base.OnPropertyChanged(e);
    }

    /// <summary>
    /// Validates a property again if it was validated before. Used by generated code when a property that another
    /// one depends on changes, e.g. the target of a <see cref="CompareAttribute"/>.
    /// Properties that were never validated stay untouched, so an empty form shows no errors
    /// </summary>
    /// <param name="propertyName"> The dependent property </param>
    [EditorBrowsable(EditorBrowsableState.Never)]
    protected void RevalidateIfValidated(string propertyName)
    {
        if (_validatedProperties.Contains(propertyName))
            TryValidateProperty(propertyName);
    }

    /// <summary> Validates the properties that depend on a changed property. Overridden by generated code </summary>
    /// <param name="propertyName"> The changed property </param>
    [EditorBrowsable(EditorBrowsableState.Never)]
    protected virtual void ValidateDependentPropertiesCore(string propertyName) { }

    /// <summary> Creates the context passed to validation attributes. Used by generated code </summary>
    /// <param name="propertyName"> The name of the validated property </param>
    /// <param name="displayName"> The display name of the validated property </param>
    /// <returns> The validation context </returns>
    [EditorBrowsable(EditorBrowsableState.Never)]
    protected ValidationContext CreateValidationContext(string propertyName, string? displayName) =>
        new(this, string.IsNullOrEmpty(displayName) ? propertyName : displayName, _serviceProvider, _items)
        {
            MemberName = propertyName,
        };

    /// <summary> Applies the default message to the result of a <see cref="CustomValidationAttribute"/> method. Used by generated code </summary>
    /// <param name="result"> The result returned by the validation method </param>
    /// <param name="errorMessage"> The <see cref="ValidationAttribute.ErrorMessage"/> set on the attribute </param>
    /// <param name="context"> The validation context </param>
    /// <returns> The completed result </returns>
    [EditorBrowsable(EditorBrowsableState.Never)]
    protected static ValidationResult? CompleteCustomValidationResult(
        ValidationResult? result,
        string? errorMessage,
        ValidationContext context
    )
    {
        ArgumentNullException.ThrowIfNull(context);
        if (result is null || !string.IsNullOrEmpty(result.ErrorMessage))
            return result;
        string message = string.Format(
            CultureInfo.CurrentCulture,
            errorMessage ?? "{0} is not valid.",
            context.DisplayName
        );
        return new ValidationResult(message, result.MemberNames);
    }

    /// <summary>
    /// Gets the message format of an attribute the generated code implements itself, with the resource lookup and
    /// checks of <see cref="ValidationAttribute"/>. Used by generated code
    /// </summary>
    /// <param name="errorMessage"> The <see cref="ValidationAttribute.ErrorMessage"/> set on the attribute </param>
    /// <param name="resourceType"> The <see cref="ValidationAttribute.ErrorMessageResourceType"/> set on the attribute </param>
    /// <param name="resourceName"> The <see cref="ValidationAttribute.ErrorMessageResourceName"/> set on the attribute </param>
    /// <returns> The message format in the current UI culture </returns>
    [EditorBrowsable(EditorBrowsableState.Never)]
    protected static string GetErrorMessage(
        string? errorMessage,
        [DynamicallyAccessedMembers(
            DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.NonPublicProperties
        )]
            Type? resourceType,
        string? resourceName
    )
    {
        var attribute = new MessageAttribute
        {
            ErrorMessageResourceType = resourceType,
            ErrorMessageResourceName = resourceName,
        };
        if (errorMessage is not null)
            attribute.ErrorMessage = errorMessage;
        return attribute.MessageFormat;
    }

    /// <summary> Implements <see cref="CompareAttribute"/> without reflection. Used by generated code </summary>
    /// <param name="value"> The value of the validated property </param>
    /// <param name="otherValue"> The value of the compared property </param>
    /// <param name="otherDisplayName"> The display name of the compared property </param>
    /// <param name="errorMessage"> The <see cref="ValidationAttribute.ErrorMessage"/> set on the attribute </param>
    /// <param name="context"> The validation context </param>
    /// <returns> <see langword="null"/> if both values are equal, an error otherwise </returns>
    [EditorBrowsable(EditorBrowsableState.Never)]
    protected static ValidationResult? ValidateCompare(
        object? value,
        object? otherValue,
        string otherDisplayName,
        string? errorMessage,
        ValidationContext context
    )
    {
        ArgumentNullException.ThrowIfNull(context);
        if (Equals(value, otherValue))
            return null;
        string message = string.Format(
            CultureInfo.CurrentCulture,
            errorMessage ?? "'{0}' and '{1}' do not match.",
            context.DisplayName,
            otherDisplayName
        );
        return new ValidationResult(message, context.MemberName is { } memberName ? [memberName] : null);
    }

    /// <summary> Validates the current value of a property. Overridden by generated code </summary>
    /// <param name="propertyName"> The property name </param>
    /// <param name="errors"> The collection to add errors to </param>
    /// <returns> <see langword="true"/> if the type has validation for <paramref name="propertyName"/> </returns>
    [EditorBrowsable(EditorBrowsableState.Never)]
    protected virtual bool TryValidatePropertyCore(string propertyName, ICollection<ValidationResult> errors) => false;

    /// <summary> Validates all properties with validation attributes. Overridden by generated code </summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    protected virtual void ValidateAllPropertiesCore() { }

    /// <summary> Exposes the message lookup of <see cref="ValidationAttribute"/>, which is protected </summary>
    private sealed class MessageAttribute : ValidationAttribute
    {
        public string MessageFormat => ErrorMessageString;
    }

    private bool TryValidateProperty(string propertyName)
    {
        List<ValidationResult> errors = [];
        if (!TryValidatePropertyCore(propertyName, errors))
            return false;
        _validatedProperties.Add(propertyName);
        SetErrors(propertyName, errors);
        return true;
    }

    private static bool HaveSameMessages(ValidationResult[] left, ValidationResult[] right)
    {
        if (left.Length != right.Length)
            return false;
        for (var i = 0; i < left.Length; i++)
        {
            if (!string.Equals(left[i].ErrorMessage, right[i].ErrorMessage, StringComparison.Ordinal))
                return false;
        }
        return true;
    }
}
