using System.Text.Json.Serialization;
using PayPalServerSdk.Core.Models;

namespace PayPalServerSdk.Models;

/// <summary>
/// An API resource denoting a request to securely store a SEPA Debit.
/// </summary>
public record SepaDebitRequest
{
    /// <summary>
    /// Customizes the payer experience during the approval process for the SEPA Debit payment.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("experience_context")]
    public SepaDebitExperienceContext? ExperienceContext { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
