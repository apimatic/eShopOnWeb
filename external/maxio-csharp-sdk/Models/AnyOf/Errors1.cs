using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using MaxioAdvancedBilling.Core.Extensions;
using MaxioAdvancedBilling.Core.Models;

namespace MaxioAdvancedBilling.Models.AnyOf;

[JsonConverter(typeof(Errors1Converter))]
public record Errors1
{
    private readonly Optional<CustomerError> _customerErrorValue;

    private readonly Optional<IReadOnlyList<string>> _listOfStringValue;

    private Errors1(Optional<CustomerError> customerErrorValue, Optional<IReadOnlyList<string>> listOfStringValue)
    {
        _customerErrorValue = customerErrorValue;
        _listOfStringValue = listOfStringValue;
    }

    public static Errors1 CustomerError(CustomerError value) =>
        new(Optional<CustomerError>.Some(value), default);

    public static Errors1 ListOfString(IReadOnlyList<string> value) =>
        new(default, Optional<IReadOnlyList<string>>.Some(value));

    public bool TryGetCustomerError(out CustomerError value) => _customerErrorValue.TryGetValue(out value);

    public bool TryGetListOfString(out IReadOnlyList<string> value) =>
        _listOfStringValue.TryGetValue(out value);

    public static implicit operator Errors1(CustomerError value) => CustomerError(value);
}

file sealed class Errors1Converter : JsonConverter<Errors1>
{
    public override Errors1 Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var doc = JsonDocument.ParseValue(ref reader);
        var root = doc.RootElement;
        if (JsonSerializer.TryDeserialize<CustomerError>(root, options, out var customerErrorValue))
        {
            return Errors1.CustomerError(customerErrorValue);
        }
        if (JsonSerializer.TryDeserialize<IReadOnlyList<string>>(root, options, out var listOfStringValue))
        {
            return Errors1.ListOfString(listOfStringValue);
        }
        throw new JsonException($"JSON does not match CustomerError or IReadOnlyList<string> schemas: {root.ToString()}");
    }

    public override void Write(Utf8JsonWriter writer, Errors1 value, JsonSerializerOptions options)
    {
        if (value.TryGetCustomerError(out var customerErrorValue))
        {
            JsonSerializer.Serialize(writer, customerErrorValue, options);
        }
        else if (value.TryGetListOfString(out var listOfStringValue))
        {
            JsonSerializer.Serialize(writer, listOfStringValue, options);
        }
        else
        {
            throw new JsonException($"{nameof(Errors1)} contains no valid value to serialize.");
        }
    }
}
