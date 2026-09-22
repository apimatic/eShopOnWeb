using System.Text.Json.Serialization;
using TwilioSdk.Core.Models;

namespace TwilioSdk.Models;

/// <summary>
/// Know Your Traffic (KYT) metrics focused on outbound carrier performance and trust signals for the report period.
/// </summary>
public record Kyt
{
    /// <summary>
    /// KYT metrics for outbound carrier calling.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("outbound_carrier_calling")]
    public OutboundCarrierCalling? OutboundCarrierCalling { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
