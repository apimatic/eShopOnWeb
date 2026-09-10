using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using MaxioAdvancedBilling.Core.Extensions;
using MaxioAdvancedBilling.Core.Models;

namespace MaxioAdvancedBilling.Models.AnyOf;

[JsonConverter(typeof(RefundPrepaymentErrorResponseConverter))]
public record RefundPrepaymentErrorResponse
{
    private readonly Optional<RefundPrepaymentAggregatedErrorsResponse> _refundPrepaymentAggregatedErrorsResponseValue;

    private readonly Optional<ErrorStringMapResponse1> _errorStringMapResponse1Value;

    private RefundPrepaymentErrorResponse(Optional<RefundPrepaymentAggregatedErrorsResponse> refundPrepaymentAggregatedErrorsResponseValue,
        Optional<ErrorStringMapResponse1> errorStringMapResponse1Value)
    {
        _refundPrepaymentAggregatedErrorsResponseValue = refundPrepaymentAggregatedErrorsResponseValue;
        _errorStringMapResponse1Value = errorStringMapResponse1Value;
    }

    public static RefundPrepaymentErrorResponse RefundPrepaymentAggregatedErrorsResponse(RefundPrepaymentAggregatedErrorsResponse value) =>
        new(Optional<RefundPrepaymentAggregatedErrorsResponse>.Some(value), default);

    public static RefundPrepaymentErrorResponse ErrorStringMapResponse1(ErrorStringMapResponse1 value) =>
        new(default, Optional<ErrorStringMapResponse1>.Some(value));

    public bool TryGetRefundPrepaymentAggregatedErrorsResponse(out RefundPrepaymentAggregatedErrorsResponse value) =>
        _refundPrepaymentAggregatedErrorsResponseValue.TryGetValue(out value);

    public bool TryGetErrorStringMapResponse1(out ErrorStringMapResponse1 value) =>
        _errorStringMapResponse1Value.TryGetValue(out value);

    public static implicit operator RefundPrepaymentErrorResponse(RefundPrepaymentAggregatedErrorsResponse value) =>
        RefundPrepaymentAggregatedErrorsResponse(value);

    public static implicit operator RefundPrepaymentErrorResponse(ErrorStringMapResponse1 value) =>
        ErrorStringMapResponse1(value);
}

file sealed class RefundPrepaymentErrorResponseConverter : JsonConverter<RefundPrepaymentErrorResponse>
{
    public override RefundPrepaymentErrorResponse Read(ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        using var doc = JsonDocument.ParseValue(ref reader);
        var root = doc.RootElement;
        if (JsonSerializer.TryDeserialize<RefundPrepaymentAggregatedErrorsResponse>(root,
            options,
            out var refundPrepaymentAggregatedErrorsResponseValue))
        {
            return RefundPrepaymentErrorResponse.RefundPrepaymentAggregatedErrorsResponse(refundPrepaymentAggregatedErrorsResponseValue);
        }
        if (JsonSerializer.TryDeserialize<ErrorStringMapResponse1>(root,
            options,
            out var errorStringMapResponse1Value))
        {
            return RefundPrepaymentErrorResponse.ErrorStringMapResponse1(errorStringMapResponse1Value);
        }
        throw new JsonException($"JSON does not match RefundPrepaymentAggregatedErrorsResponse or ErrorStringMapResponse1 schemas: {root.ToString()}");
    }

    public override void Write(Utf8JsonWriter writer,
        RefundPrepaymentErrorResponse value,
        JsonSerializerOptions options)
    {
        if (value.TryGetRefundPrepaymentAggregatedErrorsResponse(out var refundPrepaymentAggregatedErrorsResponseValue))
        {
            JsonSerializer.Serialize(writer, refundPrepaymentAggregatedErrorsResponseValue, options);
        }
        else if (value.TryGetErrorStringMapResponse1(out var errorStringMapResponse1Value))
        {
            JsonSerializer.Serialize(writer, errorStringMapResponse1Value, options);
        }
        else
        {
            throw new JsonException($"{nameof(RefundPrepaymentErrorResponse)} contains no valid value to serialize.");
        }
    }
}
