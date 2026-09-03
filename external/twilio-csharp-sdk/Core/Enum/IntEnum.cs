using System;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;

namespace Twilio.Core.Enum;

public abstract record IntEnum<TEnum> : TypedEnum<int, TEnum>
    where TEnum : IntEnum<TEnum>
{
    private static readonly ConcurrentDictionary<Type, Func<int, TEnum>> ConstructorCache = new();

    protected IntEnum(int value) : base(value) { }

    /// <summary>
    /// Creates an instance from an int value (accepts any value during deserialization)
    /// </summary>
    protected static TEnum FromValueCore(int value)
    {
        if (KnownValues.TryGetValue(value, out var known))
            return known;

        var factory = ConstructorCache.GetOrAdd(typeof(TEnum), type =>
        {
            var constructor = type.GetConstructor(
                BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance,
                null,
                [typeof(int)],
                null
            );

            if (constructor is null)
                throw new InvalidOperationException(
                    $"Type {type.Name} must have a constructor that accepts an int parameter");

            return v => (TEnum)constructor.Invoke([v]);
        });

        return factory(value);
    }

    /// <summary>
    /// Tries to get a known value, returns false if value is not predefined
    /// </summary>
    public static bool TryGetKnownValue(int value, [NotNullWhen(true)] out TEnum? result) =>
        KnownValues.TryGetValue(value, out result);
}