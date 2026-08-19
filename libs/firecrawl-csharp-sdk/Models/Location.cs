using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace FirecrawlApi.Models;

/// <summary>
/// Location settings for the request. When specified, this will use an appropriate proxy if available and emulate the corresponding language and timezone settings. Defaults to 'US' if not specified.
/// </summary>
public record Location
{
    /// <summary>
    /// ISO 3166-1 alpha-2 country code (e.g., 'US', 'AU', 'DE', 'JP')
    /// </summary>
    [JsonPropertyName("country")]
    [RegularExpression("^[A-Z]{2}$")]
    public string? Country { get; init; } = "US";

    /// <summary>
    /// Preferred languages and locales for the request in order of priority. Defaults to the language of the specified location. See https://developer.mozilla.org/en-US/docs/Web/HTTP/Headers/Accept-Language
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("languages")]
    public IReadOnlyList<string>? Languages { get; init; }
}
