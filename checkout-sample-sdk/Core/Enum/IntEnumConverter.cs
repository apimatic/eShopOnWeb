using System;
using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PayPalServerSdk.Core.Enum;

public sealed class IntEnumConverter<TEnum> : JsonConverter<TEnum>
    where TEnum : IntEnum<TEnum>
{
    private static readonly ConcurrentDictionary<Type, Func<int, TEnum>> FromValueCoreCache = new();

    public override TEnum? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType is JsonTokenType.Null)
            return null;

        if (reader.TokenType is not JsonTokenType.Number)
            throw new JsonException($"Unexpected token {reader.TokenType} when parsing {typeToConvert.Name}. Expected Number.");

        var value = reader.GetInt32();

        var factory = FromValueCoreCache.GetOrAdd(typeToConvert, type =>
        {
            var method = type.GetMethod(
                "FromValueCore",
                BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.FlattenHierarchy,
                null,
                [typeof(int)],
                null
            );

            if (method is null)
                throw new InvalidOperationException(
                    $"Type {type.Name} must inherit from {nameof(IntEnum<>)}<T>");

            return v => (TEnum)method.Invoke(null, [v])!;
        });

        return factory(value);
    }

    public override void Write(Utf8JsonWriter writer, TEnum? value, JsonSerializerOptions options)
    {
        if (value is null)
        {
            writer.WriteNullValue();
            return;
        }

        writer.WriteNumberValue(value.Value);
    }
}