using System;
using System.Text.Json.Serialization;
using MaxioAdvancedBilling.Core.Models;
using MaxioAdvancedBilling.Models.Enums;

namespace MaxioAdvancedBilling.Models;

public record RemovePaymentEvent
{
    [JsonPropertyName("id")]
    public required long Id { get; init; }

    [JsonPropertyName("timestamp")]
    public required DateTimeOffset Timestamp { get; init; }

    [JsonPropertyName("invoice")]
    public required Invoice Invoice { get; init; }

    [JsonPropertyName("event_type")]
    public InvoiceEventType EventType { get; init; } = InvoiceEventType.RemovePayment;

    /// <summary>
    /// Example schema for an <c>remove_payment</c> event
    /// </summary>
    [JsonPropertyName("event_data")]
    public required RemovePaymentEventData EventData { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
