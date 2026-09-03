using System.Text.Json.Serialization;
using Maxio.Core.Models;

namespace Maxio.Models;

public record BankAccountVerification
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("deposit_1_in_cents")]
    public long? Deposit1InCents { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("deposit_2_in_cents")]
    public long? Deposit2InCents { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
