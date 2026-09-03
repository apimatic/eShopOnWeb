using System.Text.Json.Serialization;
using Maxio.Core.Models;

namespace Maxio.Models;

public record RenewalPreviewResponse
{
    [JsonPropertyName("renewal_preview")]
    public required RenewalPreview RenewalPreview { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
