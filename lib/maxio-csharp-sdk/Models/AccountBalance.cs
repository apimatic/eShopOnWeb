using System.Text.Json.Serialization;
using Maxio.Core.Models;

namespace Maxio.Models;

public record AccountBalance
{
    /// <summary>
    /// The balance in cents.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("balance_in_cents")]
    public long? BalanceInCents { get; init; }

    /// <summary>
    /// The automatic balance in cents.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("automatic_balance_in_cents")]
    public long? AutomaticBalanceInCents { get; init; }

    /// <summary>
    /// The remittance balance in cents.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("remittance_balance_in_cents")]
    public long? RemittanceBalanceInCents { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
