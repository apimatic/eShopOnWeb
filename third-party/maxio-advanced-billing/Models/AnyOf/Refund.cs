using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using MaxioAdvancedBilling.Core.Extensions;
using MaxioAdvancedBilling.Core.Models;

namespace MaxioAdvancedBilling.Models.AnyOf;

[JsonConverter(typeof(RefundConverter))]
public record Refund
{
    private readonly Optional<RefundInvoice> _refundInvoiceValue;

    private readonly Optional<RefundConsolidatedInvoice> _refundConsolidatedInvoiceValue;

    private Refund(Optional<RefundInvoice> refundInvoiceValue,
        Optional<RefundConsolidatedInvoice> refundConsolidatedInvoiceValue)
    {
        _refundInvoiceValue = refundInvoiceValue;
        _refundConsolidatedInvoiceValue = refundConsolidatedInvoiceValue;
    }

    public static Refund RefundInvoice(RefundInvoice value) =>
        new(Optional<RefundInvoice>.Some(value), default);

    public static Refund RefundConsolidatedInvoice(RefundConsolidatedInvoice value) =>
        new(default, Optional<RefundConsolidatedInvoice>.Some(value));

    public bool TryGetRefundInvoice(out RefundInvoice value) => _refundInvoiceValue.TryGetValue(out value);

    public bool TryGetRefundConsolidatedInvoice(out RefundConsolidatedInvoice value) =>
        _refundConsolidatedInvoiceValue.TryGetValue(out value);

    public static implicit operator Refund(RefundInvoice value) => RefundInvoice(value);

    public static implicit operator Refund(RefundConsolidatedInvoice value) => RefundConsolidatedInvoice(value);
}

file sealed class RefundConverter : JsonConverter<Refund>
{
    public override Refund Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var doc = JsonDocument.ParseValue(ref reader);
        var root = doc.RootElement;
        if (JsonSerializer.TryDeserialize<RefundInvoice>(root, options, out var refundInvoiceValue))
        {
            return Refund.RefundInvoice(refundInvoiceValue);
        }
        if (JsonSerializer.TryDeserialize<RefundConsolidatedInvoice>(root,
            options,
            out var refundConsolidatedInvoiceValue))
        {
            return Refund.RefundConsolidatedInvoice(refundConsolidatedInvoiceValue);
        }
        throw new JsonException($"JSON does not match RefundInvoice or RefundConsolidatedInvoice schemas: {root.ToString()}");
    }

    public override void Write(Utf8JsonWriter writer, Refund value, JsonSerializerOptions options)
    {
        if (value.TryGetRefundInvoice(out var refundInvoiceValue))
        {
            JsonSerializer.Serialize(writer, refundInvoiceValue, options);
        }
        else if (value.TryGetRefundConsolidatedInvoice(out var refundConsolidatedInvoiceValue))
        {
            JsonSerializer.Serialize(writer, refundConsolidatedInvoiceValue, options);
        }
        else
        {
            throw new JsonException($"{nameof(Refund)} contains no valid value to serialize.");
        }
    }
}
