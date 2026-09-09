using System;
using System.Text.Json.Serialization;
using MaxioAdvancedBilling.Core.Models;
using MaxioAdvancedBilling.Models.Enums;

namespace MaxioAdvancedBilling.Models;

public record ChangeChargebackStatusEvent
{
    [JsonPropertyName("id")]
    public required long Id { get; init; }

    [JsonPropertyName("timestamp")]
    public required DateTimeOffset Timestamp { get; init; }

    [JsonPropertyName("invoice")]
    public required Invoice Invoice { get; init; }

    [JsonPropertyName("event_type")]
    public InvoiceEventType EventType { get; init; } = InvoiceEventType.ChangeChargebackStatus;

    /// <summary>
    /// Example schema for an <c>change_chargeback_status</c> event
    /// </summary>
    [JsonPropertyName("event_data")]
    public required ChangeChargebackStatusEventData EventData { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
