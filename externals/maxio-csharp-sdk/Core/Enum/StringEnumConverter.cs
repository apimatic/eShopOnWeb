using System;
using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Maxio.Core.Enum;

internal sealed class StringEnumConverter<TEnum> : JsonConverter<TEnum> where TEnum : StringEnum<TEnum>
{
    private static readonly ConcurrentDictionary<Type, Func<string, TEnum>> FromValueCoreCache = new();

    public override TEnum? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType is JsonTokenType.Null)
            return null;

        if (reader.TokenType is not JsonTokenType.String)
            throw new JsonException($"Unexpected token {reader.TokenType} when parsing {typeToConvert.Name}. Expected String.");

        var value = reader.GetString() ?? string.Empty;

        var factory = FromValueCoreCache.GetOrAdd(typeToConvert, type =>
        {
            var method = type.GetMethod(
                "FromValueCore",
                BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.FlattenHierarchy,
                null,
                [typeof(string)],
                null
            );

            if (method is null)
                throw new InvalidOperationException(
                    $"Type {type.Name} must inherit from {nameof(StringEnum<>)}<T>");

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

        writer.WriteStringValue(value.Value);
    }
}