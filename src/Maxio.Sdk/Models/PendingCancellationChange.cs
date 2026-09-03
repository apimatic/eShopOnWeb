using System;
using System.Text.Json.Serialization;
using Maxio.Core.Models;

namespace Maxio.Models;

public record PendingCancellationChange
{
    [JsonPropertyName("cancellation_state")]
    public required string CancellationState { get; init; }

    [JsonPropertyName("cancels_at")]
    public required DateTimeOffset CancelsAt { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
