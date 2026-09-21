using System;
using System.Text.Json.Serialization;
using MaxioAdvancedBilling.Core.Models;

namespace MaxioAdvancedBilling.Models;

public record PendingCancellationChange
{
    [JsonPropertyName("cancellation_state")]
    public required string CancellationState { get; init; }

    [JsonPropertyName("cancels_at")]
    public required DateTimeOffset CancelsAt { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
