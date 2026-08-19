using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using FirecrawlApi.Core.Validation.Attributes;
using FirecrawlApi.Models.Enums;

namespace FirecrawlApi.Models;

public record InteractExecuteRequest
{
    /// <summary>
    /// Code to execute in the browser sandbox
    /// </summary>
    [JsonPropertyName("code")]
    [StringLength(100000, MinimumLength = 1)]
    public required string Code { get; init; }

    /// <summary>
    /// Language of the code to execute. Use <c>node</c> for JavaScript or <c>bash</c> for agent-browser CLI commands.
    /// </summary>
    [JsonPropertyName("language")]
    public Language? Language { get; init; } = Language.Node;

    /// <summary>
    /// Execution timeout in seconds
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("timeout")]
    [Minimum(1)]
    [Maximum(300)]
    public int? Timeout { get; init; }
}
