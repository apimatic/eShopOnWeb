using System;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Twilio.Core.Models;

namespace Twilio.Models;

public record NumbersV1AvailablePhoneNumber
{
    /// <summary>
    /// The phone number in E.164 format.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("Did")]
    public string? Did { get; init; }

    /// <summary>
    /// The unique string that identifies the inventory DID resource.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("InventoryDidSid")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^ID[0-9a-fA-F]{32}$")]
    public string? InventoryDidSid { get; init; }

    /// <summary>
    /// A human-readable phone number in national format.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("FriendlyName")]
    public string? FriendlyName { get; init; }

    /// <summary>
    /// The type of phone number. Can be Local, Mobile, TollFree, etc.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("Type")]
    public string? Type { get; init; }

    /// <summary>
    /// The North American Numbering Plan (NANP) area code of the phone number.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("Npa")]
    public string? Npa { get; init; }

    /// <summary>
    /// The three-digit exchange code of the phone number.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("Nxx")]
    public string? Nxx { get; init; }

    /// <summary>
    /// Whether the phone number is locked for purchase.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("Locked")]
    public bool? Locked { get; init; }

    /// <summary>
    /// The Unix timestamp when the phone number lock expires.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("LockedUntil")]
    public int? LockedUntil { get; init; }

    /// <summary>
    /// The set of Boolean properties that describes the SMS, MMS, Voice, and Fax capabilities of the phone number.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("Capabilities")]
    public Capabilities1? Capabilities { get; init; }

    /// <summary>
    /// The geographic information associated with the phone number.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("Geography")]
    public Geography? Geography { get; init; }

    /// <summary>
    /// The type of Address resource the phone number requires.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("AddressRequirements")]
    public string? AddressRequirements { get; init; }

    /// <summary>
    /// The certifications required for the phone number.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("Certifications")]
    public Certifications? Certifications { get; init; }

    /// <summary>
    /// The billing information for the phone number.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("Billing")]
    public Billing? Billing { get; init; }

    /// <summary>
    /// The date and time in GMT when the resource was created specified in ISO 8601 format.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("DateCreated")]
    public DateTimeOffset? DateCreated { get; init; }

    /// <summary>
    /// The date and time in GMT when the resource was last updated specified in ISO 8601 format.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("DateUpdated")]
    public DateTimeOffset? DateUpdated { get; init; }

    /// <summary>
    /// Whether the phone number is in beta.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("Beta")]
    public bool? Beta { get; init; }

    /// <summary>
    /// Whether the phone number can handle emergency calls.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("VoiceEmergencyCapable")]
    public bool? VoiceEmergencyCapable { get; init; }

    /// <summary>
    /// The flags that describe the phone number features.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("Flags")]
    public Flags? Flags { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
