using System.Text.Json.Serialization;
using MaxioAdvancedBilling.Core.Models;

namespace MaxioAdvancedBilling.Models;

public record RenewalPreviewResponse
{
    [JsonPropertyName("renewal_preview")]
    public required RenewalPreview RenewalPreview { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
