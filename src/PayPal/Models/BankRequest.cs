using System.Text.Json.Serialization;
using PayPal.Core.Models;

namespace PayPal.Models;

/// <summary>
/// A Resource representing a request to vault a Bank used for ACH Debit.
/// </summary>
public record BankRequest
{
    /// <summary>
    /// A Resource representing a request to vault a ACH Debit.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("ach_debit")]
    public object? AchDebit { get; init; }

    /// <summary>
    /// An API resource denoting a request to securely store a SEPA Debit.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("sepa_debit")]
    public SepaDebitRequest? SepaDebit { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
