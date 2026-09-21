using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using MaxioAdvancedBilling.Core.Extensions;
using MaxioAdvancedBilling.Core.Models;

namespace MaxioAdvancedBilling.Models.AnyOf;

[JsonConverter(typeof(PrepaidConfigurationErrorResponseConverter))]
public record PrepaidConfigurationErrorResponse
{
    private readonly Optional<ErrorStringMapResponse1> _errorStringMapResponse1Value;

    private readonly Optional<ErrorListResponse1> _errorListResponse1Value;

    private PrepaidConfigurationErrorResponse(Optional<ErrorStringMapResponse1> errorStringMapResponse1Value,
        Optional<ErrorListResponse1> errorListResponse1Value)
    {
        _errorStringMapResponse1Value = errorStringMapResponse1Value;
        _errorListResponse1Value = errorListResponse1Value;
    }

    public static PrepaidConfigurationErrorResponse ErrorStringMapResponse1(ErrorStringMapResponse1 value) =>
        new(Optional<ErrorStringMapResponse1>.Some(value), default);

    public static PrepaidConfigurationErrorResponse ErrorListResponse1(ErrorListResponse1 value) =>
        new(default, Optional<ErrorListResponse1>.Some(value));

    public bool TryGetErrorStringMapResponse1(out ErrorStringMapResponse1 value) =>
        _errorStringMapResponse1Value.TryGetValue(out value);

    public bool TryGetErrorListResponse1(out ErrorListResponse1 value) =>
        _errorListResponse1Value.TryGetValue(out value);

    public static implicit operator PrepaidConfigurationErrorResponse(ErrorStringMapResponse1 value) =>
        ErrorStringMapResponse1(value);

    public static implicit operator PrepaidConfigurationErrorResponse(ErrorListResponse1 value) =>
        ErrorListResponse1(value);
}

file sealed class PrepaidConfigurationErrorResponseConverter : JsonConverter<PrepaidConfigurationErrorResponse>
{
    public override PrepaidConfigurationErrorResponse Read(ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        using var doc = JsonDocument.ParseValue(ref reader);
        var root = doc.RootElement;
        if (JsonSerializer.TryDeserialize<ErrorStringMapResponse1>(root,
            options,
            out var errorStringMapResponse1Value))
        {
            return PrepaidConfigurationErrorResponse.ErrorStringMapResponse1(errorStringMapResponse1Value);
        }
        if (JsonSerializer.TryDeserialize<ErrorListResponse1>(root, options, out var errorListResponse1Value))
        {
            return PrepaidConfigurationErrorResponse.ErrorListResponse1(errorListResponse1Value);
        }
        throw new JsonException($"JSON does not match ErrorStringMapResponse1 or ErrorListResponse1 schemas: {root.ToString()}");
    }

    public override void Write(Utf8JsonWriter writer,
        PrepaidConfigurationErrorResponse value,
        JsonSerializerOptions options)
    {
        if (value.TryGetErrorStringMapResponse1(out var errorStringMapResponse1Value))
        {
            JsonSerializer.Serialize(writer, errorStringMapResponse1Value, options);
        }
        else if (value.TryGetErrorListResponse1(out var errorListResponse1Value))
        {
            JsonSerializer.Serialize(writer, errorListResponse1Value, options);
        }
        else
        {
            throw new JsonException($"{nameof(PrepaidConfigurationErrorResponse)} contains no valid value to serialize.");
        }
    }
}
