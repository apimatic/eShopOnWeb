using System.Collections.Generic;
using System.Text.Json.Serialization;
using Maxio.Core.Models;

namespace Maxio.Models;

/// <summary>
/// Error which contains list of messages.
/// </summary>
public record ErrorListResponse1
{
    [JsonPropertyName("errors")]
    public required IReadOnlyList<string> Errors { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
