using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using TwilioSdk.Core.Models;

namespace TwilioSdk.Models;

/// <summary>
/// Individual keyword configuration
/// </summary>
public record KeywordItem
{
    /// <summary>
    /// The actual keyword text
    /// </summary>
    [JsonPropertyName("keyword")]
    [StringLength(34, MinimumLength = 1)]
    [RegularExpression("^[A-Z0-9]+$")]
    public required string Keyword { get; init; }

    /// <summary>
    /// Indicates whether this keyword is reserved by the system and cannot be modified
    /// </summary>
    [JsonPropertyName("reserved")]
    public required bool Reserved { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
