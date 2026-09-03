using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using TwilioSdk.Core.Models;

namespace TwilioSdk.Models;

/// <summary>
/// Configuration for a specific keyword type (STOP, START, HELP, etc.)
/// </summary>
public record KeywordTypeConfig
{
    /// <summary>
    /// List of keywords associated with this keyword type
    /// </summary>
    [JsonPropertyName("keywords")]
    [MinLength(1)]
    public required IReadOnlyList<KeywordItem> Keywords { get; init; }

    /// <summary>
    /// The response message sent when any keyword of this type is received
    /// </summary>
    [JsonPropertyName("message")]
    [StringLength(1600, MinimumLength = 1)]
    public required string Message { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
