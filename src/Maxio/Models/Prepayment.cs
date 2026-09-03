using System;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Maxio.Core.Models;
using Maxio.Models.Enums;

namespace Maxio.Models;

public record Prepayment
{
    [JsonPropertyName("id")]
    public required int Id { get; init; }

    [JsonPropertyName("subscription_id")]
    public required int SubscriptionId { get; init; }

    [JsonPropertyName("amount_in_cents")]
    public required long AmountInCents { get; init; }

    [JsonPropertyName("remaining_amount_in_cents")]
    public required long RemainingAmountInCents { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("refunded_amount_in_cents")]
    public long? RefundedAmountInCents { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("details")]
    [MinLength(1)]
    public string? Details { get; init; }

    [JsonPropertyName("external")]
    public required bool External { get; init; }

    [JsonPropertyName("memo")]
    [MinLength(1)]
    public required string Memo { get; init; }

    /// <summary>
    /// The payment type of the prepayment.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("payment_type")]
    public PrepaymentMethod? PaymentType { get; init; }

    [JsonPropertyName("created_at")]
    public required DateTimeOffset CreatedAt { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
