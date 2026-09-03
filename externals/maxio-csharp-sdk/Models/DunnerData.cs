using System;
using System.Text.Json.Serialization;
using Maxio.Core.Models;

namespace Maxio.Models;

public record DunnerData
{
    [JsonPropertyName("state")]
    public required string State { get; init; }

    [JsonPropertyName("subscription_id")]
    public required int SubscriptionId { get; init; }

    [JsonPropertyName("revenue_at_risk_in_cents")]
    public required long RevenueAtRiskInCents { get; init; }

    [JsonPropertyName("created_at")]
    public required DateTimeOffset CreatedAt { get; init; }

    [JsonPropertyName("attempts")]
    public required int Attempts { get; init; }

    [JsonPropertyName("last_attempted_at")]
    public required DateTimeOffset LastAttemptedAt { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
