using System;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;

namespace PayPal.Core.Enum;

public abstract record StringEnum<TEnum> : TypedEnum<string, TEnum> where TEnum : StringEnum<TEnum>
{
    private static readonly ConcurrentDictionary<Type, Func<string, TEnum>> ConstructorCache = new();

    protected StringEnum(string value) : base(value) { }

    /// <summary>
    /// Creates an instance from a string value (accepts any value during deserialization)
    /// </summary>
    protected static TEnum FromValueCore(string value)
    {
        if (KnownValues.TryGetValue(value, out var known))
            return known;

        var factory = ConstructorCache.GetOrAdd(typeof(TEnum), type =>
        {
            var constructor = type.GetConstructor(
                BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance,
                null,
                [typeof(string)],
                null
            );

            if (constructor is null)
                throw new InvalidOperationException(
                    $"Type {type.Name} must have a constructor that accepts a string parameter");

            return v => (TEnum)constructor.Invoke([v]);
        });

        return factory(value);
    }

    /// <summary>
    /// Tries to get a known value, returns false if value is not predefined
    /// </summary>
    public static bool TryGetKnownValue(string value, [NotNullWhen(true)] out TEnum? result) =>
        KnownValues.TryGetValue(value, out result);
}