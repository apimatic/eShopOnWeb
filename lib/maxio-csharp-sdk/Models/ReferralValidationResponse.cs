using System.Text.Json.Serialization;
using Maxio.Core.Models;

namespace Maxio.Models;

public record ReferralValidationResponse
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("referral_code")]
    public ReferralCode? ReferralCode { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
