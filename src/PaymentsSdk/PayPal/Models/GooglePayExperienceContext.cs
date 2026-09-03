using System.Text.Json.Serialization;
using PayPal.Core.Models;
using PayPal.Core.Validation;
using PayPal.Core.Validation.Attributes;

namespace PayPal.Models;

/// <summary>
/// Customizes the payer experience during the approval process for the payment.
/// </summary>
public record GooglePayExperienceContext
{
    /// <summary>
    /// Describes the URL.
    /// </summary>
    [JsonPropertyName("return_url")]
    [Format(FormatKind.Uri)]
    public required string ReturnUrl { get; init; }

    /// <summary>
    /// Describes the URL.
    /// </summary>
    [JsonPropertyName("cancel_url")]
    [Format(FormatKind.Uri)]
    public required string CancelUrl { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
