using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using PayPalServerSdk.Core.Models;

namespace PayPalServerSdk.Models;

/// <summary>
/// The incentive details.
/// </summary>
public record IncentiveInformation
{
    /// <summary>
    /// An array of incentive details.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("incentive_details")]
    [MinLength(1)]
    [MaxLength(32767)]
    public IReadOnlyList<IncentiveDetails>? IncentiveDetails { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
