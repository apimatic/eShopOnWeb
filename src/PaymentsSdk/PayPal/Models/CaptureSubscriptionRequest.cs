using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using PayPal.Core.Models;
using PayPal.Models.Enums;

namespace PayPal.Models;

/// <summary>
/// The charge amount from the subscriber.
/// </summary>
public record CaptureSubscriptionRequest
{
    /// <summary>
    /// The reason or note for the subscription charge.
    /// </summary>
    [JsonPropertyName("note")]
    [StringLength(128, MinimumLength = 1)]
    [RegularExpression("^.*$")]
    public required string Note { get; init; }

    /// <summary>
    /// The type of capture.
    /// </summary>
    [JsonPropertyName("capture_type")]
    public required CaptureType CaptureType { get; init; }

    /// <summary>
    /// The currency and amount for a financial transaction, such as a balance or payment due.
    /// </summary>
    [JsonPropertyName("amount")]
    public required Money Amount { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
