using System.Text.Json.Serialization;
using PayPalServerSdk.Core.Models;
using PayPalServerSdk.Core.Validation;
using PayPalServerSdk.Core.Validation.Attributes;

namespace PayPalServerSdk.Models;

/// <summary>
/// Customizes the payer experience during the 3DS Approval for payment.
/// </summary>
public record CardExperienceContext
{
    /// <summary>
    /// Describes the URL.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("return_url")]
    [Format(FormatKind.Uri)]
    public string? ReturnUrl { get; init; }

    /// <summary>
    /// Describes the URL.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("cancel_url")]
    [Format(FormatKind.Uri)]
    public string? CancelUrl { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
