using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using PayPalServerSdk.Core.Models;

namespace PayPalServerSdk.Models;

/// <summary>
/// The update pricing scheme request details.
/// </summary>
public record UpdatePricingSchemesRequest
{
    /// <summary>
    /// An array of pricing schemes.
    /// </summary>
    [JsonPropertyName("pricing_schemes")]
    [MinLength(1)]
    [MaxLength(99)]
    public required IReadOnlyList<UpdatePricingScheme> PricingSchemes { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
