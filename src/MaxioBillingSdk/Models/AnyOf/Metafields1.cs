using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using MaxioAdvancedBilling.Core.Extensions;
using MaxioAdvancedBilling.Core.Models;

namespace MaxioAdvancedBilling.Models.AnyOf;

[JsonConverter(typeof(Metafields1Converter))]
public record Metafields1
{
    private readonly Optional<UpdateMetafield> _updateMetafieldValue;

    private readonly Optional<IReadOnlyList<UpdateMetafield>> _listOfUpdateMetafieldValue;

    private Metafields1(Optional<UpdateMetafield> updateMetafieldValue,
        Optional<IReadOnlyList<UpdateMetafield>> listOfUpdateMetafieldValue)
    {
        _updateMetafieldValue = updateMetafieldValue;
        _listOfUpdateMetafieldValue = listOfUpdateMetafieldValue;
    }

    public static Metafields1 UpdateMetafield(UpdateMetafield value) =>
        new(Optional<UpdateMetafield>.Some(value), default);

    public static Metafields1 ListOfUpdateMetafield(IReadOnlyList<UpdateMetafield> value) =>
        new(default, Optional<IReadOnlyList<UpdateMetafield>>.Some(value));

    public bool TryGetUpdateMetafield(out UpdateMetafield value) =>
        _updateMetafieldValue.TryGetValue(out value);

    public bool TryGetListOfUpdateMetafield(out IReadOnlyList<UpdateMetafield> value) =>
        _listOfUpdateMetafieldValue.TryGetValue(out value);

    public static implicit operator Metafields1(UpdateMetafield value) => UpdateMetafield(value);
}

file sealed class Metafields1Converter : JsonConverter<Metafields1>
{
    public override Metafields1 Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var doc = JsonDocument.ParseValue(ref reader);
        var root = doc.RootElement;
        if (JsonSerializer.TryDeserialize<UpdateMetafield>(root, options, out var updateMetafieldValue))
        {
            return Metafields1.UpdateMetafield(updateMetafieldValue);
        }
        if (JsonSerializer.TryDeserialize<IReadOnlyList<UpdateMetafield>>(root,
            options,
            out var listOfUpdateMetafieldValue))
        {
            return Metafields1.ListOfUpdateMetafield(listOfUpdateMetafieldValue);
        }
        throw new JsonException($"JSON does not match UpdateMetafield or IReadOnlyList<UpdateMetafield> schemas: {root.ToString()}");
    }

    public override void Write(Utf8JsonWriter writer, Metafields1 value, JsonSerializerOptions options)
    {
        if (value.TryGetUpdateMetafield(out var updateMetafieldValue))
        {
            JsonSerializer.Serialize(writer, updateMetafieldValue, options);
        }
        else if (value.TryGetListOfUpdateMetafield(out var listOfUpdateMetafieldValue))
        {
            JsonSerializer.Serialize(writer, listOfUpdateMetafieldValue, options);
        }
        else
        {
            throw new JsonException($"{nameof(Metafields1)} contains no valid value to serialize.");
        }
    }
}
