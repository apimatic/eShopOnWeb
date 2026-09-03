using System.Text.Json.Serialization;
using Twilio.Core.Models;

namespace Twilio.Models;

public record Address
{
    /// <summary>
    /// The street address, ex: 101 Spear St
    /// </summary>
    [JsonPropertyName("street")]
    public required string Street { get; init; }

    /// <summary>
    /// The building information, ex : 5th floor.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("street_2")]
    public string? Street2 { get; init; }

    /// <summary>
    /// The city name, ex: San Francisco.
    /// </summary>
    [JsonPropertyName("city")]
    public required string City { get; init; }

    /// <summary>
    /// The state name, ex: CA or California. Note this should match the losing carrier’s information exactly. So if they spell out the entire state’s name instead of abbreviating it, please do so.
    /// </summary>
    [JsonPropertyName("state")]
    public required string State { get; init; }

    /// <summary>
    /// The zip code, ex: 94105.
    /// </summary>
    [JsonPropertyName("zip")]
    public required string Zip { get; init; }

    /// <summary>
    /// The country, ex: USA.
    /// </summary>
    [JsonPropertyName("country")]
    public required string Country { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
