using System.Text.Json.Serialization;
using Maxio.Core.Models;
using Maxio.Models.Enums;

namespace Maxio.Models;

public record TaxConfiguration
{
    [JsonPropertyName("kind")]
    public TaxConfigurationKind? Kind { get; init; } = TaxConfigurationKind.Custom;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("destination_address")]
    public TaxDestinationAddress? DestinationAddress { get; init; }

    /// <summary>
    /// Returns <c>true</c> when Chargify has been properly configured to charge tax using the specified tax system. More details about taxes: https://maxio.zendesk.com/hc/en-us/articles/24287012608909-Taxes-Overview
    /// </summary>
    [JsonPropertyName("fully_configured")]
    public bool? FullyConfigured { get; init; } = false;

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
