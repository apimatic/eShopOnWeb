using System.Text.Json.Serialization;
using PayPalServerSdk.Core.Models;
using PayPalServerSdk.Core.Validation;
using PayPalServerSdk.Core.Validation.Attributes;

namespace PayPalServerSdk.Models;

/// <summary>
/// Customizes the payer experience during the approval process for the payment.
/// </summary>
public record ApplePayExperienceContext
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
