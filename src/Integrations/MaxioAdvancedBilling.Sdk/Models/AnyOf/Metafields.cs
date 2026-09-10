using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using MaxioAdvancedBilling.Core.Extensions;
using MaxioAdvancedBilling.Core.Models;

namespace MaxioAdvancedBilling.Models.AnyOf;

[JsonConverter(typeof(MetafieldsConverter))]
public record Metafields
{
    private readonly Optional<CreateMetafield> _createMetafieldValue;

    private readonly Optional<IReadOnlyList<CreateMetafield>> _listOfCreateMetafieldValue;

    private Metafields(Optional<CreateMetafield> createMetafieldValue,
        Optional<IReadOnlyList<CreateMetafield>> listOfCreateMetafieldValue)
    {
        _createMetafieldValue = createMetafieldValue;
        _listOfCreateMetafieldValue = listOfCreateMetafieldValue;
    }

    public static Metafields CreateMetafield(CreateMetafield value) =>
        new(Optional<CreateMetafield>.Some(value), default);

    public static Metafields ListOfCreateMetafield(IReadOnlyList<CreateMetafield> value) =>
        new(default, Optional<IReadOnlyList<CreateMetafield>>.Some(value));

    public bool TryGetCreateMetafield(out CreateMetafield value) =>
        _createMetafieldValue.TryGetValue(out value);

    public bool TryGetListOfCreateMetafield(out IReadOnlyList<CreateMetafield> value) =>
        _listOfCreateMetafieldValue.TryGetValue(out value);

    public static implicit operator Metafields(CreateMetafield value) => CreateMetafield(value);
}

file sealed class MetafieldsConverter : JsonConverter<Metafields>
{
    public override Metafields Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var doc = JsonDocument.ParseValue(ref reader);
        var root = doc.RootElement;
        if (JsonSerializer.TryDeserialize<CreateMetafield>(root, options, out var createMetafieldValue))
        {
            return Metafields.CreateMetafield(createMetafieldValue);
        }
        if (JsonSerializer.TryDeserialize<IReadOnlyList<CreateMetafield>>(root,
            options,
            out var listOfCreateMetafieldValue))
        {
            return Metafields.ListOfCreateMetafield(listOfCreateMetafieldValue);
        }
        throw new JsonException($"JSON does not match CreateMetafield or IReadOnlyList<CreateMetafield> schemas: {root.ToString()}");
    }

    public override void Write(Utf8JsonWriter writer, Metafields value, JsonSerializerOptions options)
    {
        if (value.TryGetCreateMetafield(out var createMetafieldValue))
        {
            JsonSerializer.Serialize(writer, createMetafieldValue, options);
        }
        else if (value.TryGetListOfCreateMetafield(out var listOfCreateMetafieldValue))
        {
            JsonSerializer.Serialize(writer, listOfCreateMetafieldValue, options);
        }
        else
        {
            throw new JsonException($"{nameof(Metafields)} contains no valid value to serialize.");
        }
    }
}
