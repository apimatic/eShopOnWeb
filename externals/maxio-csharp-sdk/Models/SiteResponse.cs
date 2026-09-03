using System.Text.Json.Serialization;
using Maxio.Core.Models;

namespace Maxio.Models;

public record SiteResponse
{
    [JsonPropertyName("site")]
    public required Site Site { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
