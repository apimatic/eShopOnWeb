using System.Collections.Generic;
using System.Text.Json.Serialization;
using TwilioSdk.Core.Models;

namespace TwilioSdk.Models;

public record NumbersV1A2PRegistrationDetailsCampaignList
{
    /// <summary>
    /// List of A2P registration details for numbers in the campaign
    /// </summary>
    [JsonPropertyName("data")]
    public required IReadOnlyList<NumbersV1A2PRegistrationDetails> Data { get; init; }

    /// <summary>
    /// Token for pagination to retrieve the next page of results
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("nextToken")]
    public string? NextToken { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
