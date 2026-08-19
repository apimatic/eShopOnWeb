using System.Text.Json.Serialization;

namespace FirecrawlApi.Models;

public record SearchResponse
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("success")]
    public bool? Success { get; init; }

    /// <summary>
    /// The search results. The arrays available will depend on the sources you specified in the request. By default, the <c>web</c> array will be returned.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("data")]
    public Data8? Data { get; init; }

    /// <summary>
    /// Warning message if any issues occurred
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("warning")]
    public string? Warning { get; init; }

    /// <summary>
    /// The ID of the search job
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("id")]
    public string? Id { get; init; }

    /// <summary>
    /// The number of credits used for the search
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("creditsUsed")]
    public int? CreditsUsed { get; init; }
}
