using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using TwilioSdk.Core.Models;

namespace TwilioSdk.Models;

public record NumbersV2AddressCreate
{
    /// <summary>
    /// A human-readable description of this resource, up to 64 characters.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("friendly_name")]
    public string? FriendlyName { get; init; }

    /// <summary>
    /// The name of the customer associated with this address.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("customer_name")]
    public string? CustomerName { get; init; }

    /// <summary>
    /// The street address.
    /// </summary>
    [JsonPropertyName("street")]
    public required string Street { get; init; }

    /// <summary>
    /// The additional street address information.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("street_secondary")]
    public string? StreetSecondary { get; init; }

    /// <summary>
    /// The locality or city of this address.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("locality")]
    public string? Locality { get; init; }

    /// <summary>
    /// The state or region of this address.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("region")]
    public string? Region { get; init; }

    /// <summary>
    /// The postal code of this address.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("postal_code")]
    public string? PostalCode { get; init; }

    /// <summary>
    /// The ISO country code of this address.
    /// </summary>
    [JsonPropertyName("iso_country")]
    [StringLength(2, MinimumLength = 2)]
    public required string IsoCountry { get; init; }

    /// <summary>
    /// The source system that created this address.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("source")]
    public string? Source { get; init; }

    /// <summary>
    /// Whether to force validation of the address.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("force_validation")]
    public bool? ForceValidation { get; init; }

    /// <summary>
    /// Whether to bypass validation of the address.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("bypass_validation")]
    public bool? BypassValidation { get; init; }

    /// <summary>
    /// Whether to automatically correct the address.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("auto_correct_address")]
    public bool? AutoCorrectAddress { get; init; }

    /// <summary>
    /// Whether this address is enabled for emergency services.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("emergency_enabled")]
    public bool? EmergencyEnabled { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
