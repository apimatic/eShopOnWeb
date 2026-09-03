using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using TwilioSdk.Core.Models;

namespace TwilioSdk.Models;

public record ApiV2010AccountAddress
{
    /// <summary>
    /// The SID of the <see href="https://www.twilio.com/docs/iam/api/account">Account</see> that is responsible for the Address resource.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("account_sid")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^AC[0-9a-fA-F]{32}$")]
    public string? AccountSid { get; init; }

    /// <summary>
    /// The city in which the address is located.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("city")]
    public string? City { get; init; }

    /// <summary>
    /// The name associated with the address.This property has a maximum length of 16 4-byte characters, or 21 3-byte characters.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("customer_name")]
    public string? CustomerName { get; init; }

    /// <summary>
    /// The date and time in GMT that the resource was created specified in <see href="https://www.ietf.org/rfc/rfc2822.txt">RFC 2822</see> format.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("date_created")]
    public string? DateCreated { get; init; }

    /// <summary>
    /// The date and time in GMT that the resource was last updated specified in <see href="https://www.ietf.org/rfc/rfc2822.txt">RFC 2822</see> format.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("date_updated")]
    public string? DateUpdated { get; init; }

    /// <summary>
    /// The string that you assigned to describe the resource.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("friendly_name")]
    public string? FriendlyName { get; init; }

    /// <summary>
    /// The ISO country code of the address.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("iso_country")]
    public string? IsoCountry { get; init; }

    /// <summary>
    /// The postal code of the address.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("postal_code")]
    public string? PostalCode { get; init; }

    /// <summary>
    /// The state or region of the address.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("region")]
    public string? Region { get; init; }

    /// <summary>
    /// The unique string that that we created to identify the Address resource.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("sid")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^AD[0-9a-fA-F]{32}$")]
    public string? Sid { get; init; }

    /// <summary>
    /// The number and street address of the address.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("street")]
    public string? Street { get; init; }

    /// <summary>
    /// The URI of the resource, relative to <c>https://api.twilio.com</c>.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("uri")]
    public string? Uri { get; init; }

    /// <summary>
    /// Whether emergency calling has been enabled on this number.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("emergency_enabled")]
    public bool? EmergencyEnabled { get; init; }

    /// <summary>
    /// Whether the address has been validated to comply with local regulation. In countries that require valid addresses, an invalid address will not be accepted. <c>true</c> indicates the Address has been validated. <c>false</c> indicate the country doesn't require validation or the Address is not valid.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("validated")]
    public bool? Validated { get; init; }

    /// <summary>
    /// Whether the address has been verified to comply with regulation. In countries that require valid addresses, an invalid address will not be accepted. <c>true</c> indicates the Address has been verified. <c>false</c> indicate the country doesn't require verified or the Address is not valid.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("verified")]
    public bool? Verified { get; init; }

    /// <summary>
    /// The additional number and street address of the address.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("street_secondary")]
    public string? StreetSecondary { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
