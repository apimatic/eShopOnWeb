using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using FirecrawlApi.Models.Enums;

namespace FirecrawlApi.Models;

/// <summary>
/// Find relevant source text from the page. Returns the selected text in the response <c>highlights</c> field.
/// </summary>
public record Highlights
{
    [JsonPropertyName("type")]
    public required Type16 Type { get; init; }

    /// <summary>
    /// The text-selection query to run against the page. Maximum 10,000 characters.
    /// </summary>
    [JsonPropertyName("query")]
    [MaxLength(10000)]
    public required string Query { get; init; }
}
