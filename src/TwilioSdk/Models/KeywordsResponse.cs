using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using TwilioSdk.Core.Models;

namespace TwilioSdk.Models;

public record KeywordsResponse
{
    /// <summary>
    /// The SID of the account that owns this opt-out configuration
    /// </summary>
    [JsonPropertyName("account_sid")]
    [RegularExpression("^AC[0-9a-fA-F]{32}$")]
    public required string AccountSid { get; init; }

    /// <summary>
    /// The SID of the opt-out configuration
    /// </summary>
    [JsonPropertyName("opt_out_sid")]
    [RegularExpression("^OO[0-9a-fA-F]{32}$")]
    public required string OptOutSid { get; init; }

    /// <summary>
    /// List of keyword configurations for different keyword types
    /// </summary>
    [JsonPropertyName("config")]
    public required IReadOnlyList<KeywordConfiguration> Config { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
