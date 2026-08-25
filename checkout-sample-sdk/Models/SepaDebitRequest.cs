using System.Text.Json.Serialization;

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
}
