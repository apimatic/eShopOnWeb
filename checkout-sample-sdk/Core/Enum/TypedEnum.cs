using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace PayPalServerSdk.Core.Enum;

/// <summary>
/// Base class for type-safe enum pattern with flexible serialization
/// </summary>
/// <typeparam name="TValue">The underlying value type (e.g., string, int)</typeparam>
/// <typeparam name="TEnum">The actual enum type inheriting from this class</typeparam>
public abstract record TypedEnum<TValue, TEnum>
    where TEnum : TypedEnum<TValue, TEnum>
    where TValue : IEquatable<TValue>
{
    private static readonly Lazy<Dictionary<TValue, TEnum>> KnownValuesInternal = new(() =>
    {
        var fields = typeof(TEnum)
            .GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Where(f => f.FieldType == typeof(TEnum))
            .Select(f => (TEnum)f.GetValue(null)!);

        return CreateKnownValues(fields);
    });

    protected static Dictionary<TValue, TEnum> CreateKnownValues(IEnumerable<TEnum> values)
    {
        if (typeof(TValue) == typeof(string))
        {
            return values.ToDictionary(e => e.Value, e => e, (IEqualityComparer<TValue>)StringComparer.OrdinalIgnoreCase);
        }
        return values.ToDictionary(e => e.Value, e => e);
    }

    /// <summary>
    /// Gets the dictionary of predefined enum constants
    /// </summary>
    protected static Dictionary<TValue, TEnum> KnownValues => KnownValuesInternal.Value;

    /// <summary>
    /// The underlying value of the enum
    /// </summary>
    public TValue Value { get; init; }

    protected TypedEnum(TValue value) => Value = value;

    /// <summary>
    /// Checks if this value is one of the predefined constants
    /// </summary>
    public bool IsKnownValue() => KnownValues.ContainsKey(Value);

    /// <summary>
    /// Gets all known enum values
    /// </summary>
    public static IReadOnlyCollection<TEnum> GetKnownValues() => KnownValues.Values;

    public override string ToString() => Value.ToString() ?? string.Empty;

    // Implicit conversion to underlying value
    public static implicit operator TValue(TypedEnum<TValue, TEnum> typedEnum) => typedEnum.Value;
}