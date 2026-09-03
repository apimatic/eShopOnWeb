using System.Text.Json.Serialization;
using Maxio.Core.Models;

namespace Maxio.Models;

public record TooManyManagementLinkRequestsError
{
    [JsonPropertyName("errors")]
    public required TooManyManagementLinkRequests Errors { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
