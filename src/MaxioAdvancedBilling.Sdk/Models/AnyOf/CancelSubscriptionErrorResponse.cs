using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using MaxioAdvancedBilling.Core.Extensions;
using MaxioAdvancedBilling.Core.Models;

namespace MaxioAdvancedBilling.Models.AnyOf;

[JsonConverter(typeof(CancelSubscriptionErrorResponseConverter))]
public record CancelSubscriptionErrorResponse
{
    private readonly Optional<ErrorListResponse1> _errorListResponse1Value;

    private readonly Optional<SingleErrorResponse1> _singleErrorResponse1Value;

    private CancelSubscriptionErrorResponse(Optional<ErrorListResponse1> errorListResponse1Value,
        Optional<SingleErrorResponse1> singleErrorResponse1Value)
    {
        _errorListResponse1Value = errorListResponse1Value;
        _singleErrorResponse1Value = singleErrorResponse1Value;
    }

    public static CancelSubscriptionErrorResponse ErrorListResponse1(ErrorListResponse1 value) =>
        new(Optional<ErrorListResponse1>.Some(value), default);

    public static CancelSubscriptionErrorResponse SingleErrorResponse1(SingleErrorResponse1 value) =>
        new(default, Optional<SingleErrorResponse1>.Some(value));

    public bool TryGetErrorListResponse1(out ErrorListResponse1 value) =>
        _errorListResponse1Value.TryGetValue(out value);

    public bool TryGetSingleErrorResponse1(out SingleErrorResponse1 value) =>
        _singleErrorResponse1Value.TryGetValue(out value);

    public static implicit operator CancelSubscriptionErrorResponse(ErrorListResponse1 value) =>
        ErrorListResponse1(value);

    public static implicit operator CancelSubscriptionErrorResponse(SingleErrorResponse1 value) =>
        SingleErrorResponse1(value);
}

file sealed class CancelSubscriptionErrorResponseConverter : JsonConverter<CancelSubscriptionErrorResponse>
{
    public override CancelSubscriptionErrorResponse Read(ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        using var doc = JsonDocument.ParseValue(ref reader);
        var root = doc.RootElement;
        if (JsonSerializer.TryDeserialize<ErrorListResponse1>(root, options, out var errorListResponse1Value))
        {
            return CancelSubscriptionErrorResponse.ErrorListResponse1(errorListResponse1Value);
        }
        if (JsonSerializer.TryDeserialize<SingleErrorResponse1>(root, options, out var singleErrorResponse1Value))
        {
            return CancelSubscriptionErrorResponse.SingleErrorResponse1(singleErrorResponse1Value);
        }
        throw new JsonException($"JSON does not match ErrorListResponse1 or SingleErrorResponse1 schemas: {root.ToString()}");
    }

    public override void Write(Utf8JsonWriter writer,
        CancelSubscriptionErrorResponse value,
        JsonSerializerOptions options)
    {
        if (value.TryGetErrorListResponse1(out var errorListResponse1Value))
        {
            JsonSerializer.Serialize(writer, errorListResponse1Value, options);
        }
        else if (value.TryGetSingleErrorResponse1(out var singleErrorResponse1Value))
        {
            JsonSerializer.Serialize(writer, singleErrorResponse1Value, options);
        }
        else
        {
            throw new JsonException($"{nameof(CancelSubscriptionErrorResponse)} contains no valid value to serialize.");
        }
    }
}
