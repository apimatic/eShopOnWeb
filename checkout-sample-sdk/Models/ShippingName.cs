using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace PayPalServerSdk.Models;

/// <summary>
/// The name of the party.
/// </summary>
public record ShippingName
{
    /// <summary>
    /// When the party is a person, the party's full name.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("full_name")]
    [MaxLength(300)]
    public string? FullName { get; init; }
}
