using System.Text.Json.Serialization;
using MaxioAdvancedBilling.Core.Models;

namespace MaxioAdvancedBilling.Models;

public record TooManyManagementLinkRequestsError1
{
    [JsonPropertyName("errors")]
    public required TooManyManagementLinkRequests Errors { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
