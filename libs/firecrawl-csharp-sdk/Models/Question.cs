using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using FirecrawlApi.Models.Enums;

namespace FirecrawlApi.Models;

/// <summary>
/// Ask a natural-language question about the page. Returns the answer in the response <c>answer</c> field.
/// </summary>
public record Question
{
    [JsonPropertyName("type")]
    public required Type15 Type { get; init; }

    /// <summary>
    /// The question to answer about the page. Maximum 10,000 characters.
    /// </summary>
    [JsonPropertyName("question")]
    [MaxLength(10000)]
    public required string QuestionValue { get; init; }
}
