# Darp.CommunityToolkit.MvvmValidator

A .NET library that validates MVVM view models with DataAnnotations attributes, without the reflection that
CommunityToolkit.Mvvm's `ObservableValidator` needs. It exists so that applications using the toolkit can be trimmed and
compiled with Native AOT.

## Language

**View model**:
A class deriving from `ValidatingObservableObject`. It must be partial, so the generator can add its validation.

**Validated property**:
A property of a view model with at least one validation attribute, including attributes it inherits by overriding a
property. An `[ObservableProperty]` field counts as the property the toolkit generates for it.
_Avoid_: Validatable member

**Rule**:
One check of a validated property, created from one validation attribute. Most rules run a static instance of the
attribute. `[CustomValidation]` and `[Compare]` become direct code instead, because their attribute implementations use
reflection.

**Required rule**:
The first rule from a `RequiredAttribute`. It runs first and stops the remaining rules when it fails, like
`Validator.TryValidateProperty`.

**Dependent property**:
A validated property whose rules read another property, e.g. the property with `[Compare]`. A change of the other
property validates it again, if it was validated before.

**Generated validation**:
The partial class part the generator writes for a view model: one static field per rule and overrides of the hooks of
`ValidatingObservableObject`.
_Avoid_: Validator (it means the reflection-based `System.ComponentModel.DataAnnotations.Validator`)

## Relationships

- A **view model** has many **validated properties**; a **validated property** has many **rules**.
- **Generated validation** chains to the base type, so a view model inherits the validation of its base view models.
- The toolkit's `[ObservableProperty]` generator and this generator run independently. Generated validation refers to
  properties the toolkit generates by name, which compiles because both outputs are part of the final compilation.
