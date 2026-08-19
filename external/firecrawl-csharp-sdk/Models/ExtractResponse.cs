using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace FirecrawlApi.Models;

public record ExtractResponse
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("success")]
    public bool? Success { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("id")]
    public string? Id { get; init; }

    /// <summary>
    /// If ignoreInvalidURLs is true, this is an array containing the invalid URLs that were specified in the request. If there were no invalid URLs, this will be an empty array. If ignoreInvalidURLs is false, this field will be undefined.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("invalidURLs")]
    public IReadOnlyList<string?>? InvalidUrLs { get; init; }
}
