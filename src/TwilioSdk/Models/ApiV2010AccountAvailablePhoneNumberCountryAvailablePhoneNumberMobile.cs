using System.Text.Json.Serialization;
using TwilioSdk.Core.Models;

namespace TwilioSdk.Models;

public record ApiV2010AccountAvailablePhoneNumberCountryAvailablePhoneNumberMobile
{
    /// <summary>
    /// A formatted version of the phone number.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("friendly_name")]
    public string? FriendlyName { get; init; }

    /// <summary>
    /// The phone number in <see href="https://www.twilio.com/docs/glossary/what-e164">E.164</see> format, which consists of a + followed by the country code and subscriber number.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("phone_number")]
    public string? PhoneNumber { get; init; }

    /// <summary>
    /// The <see href="https://en.wikipedia.org/wiki/Local_access_and_transport_area">LATA</see> of this phone number. Available for only phone numbers from the US and Canada.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("lata")]
    public string? Lata { get; init; }

    /// <summary>
    /// The locality or city of this phone number's location.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("locality")]
    public string? Locality { get; init; }

    /// <summary>
    /// The <see href="https://en.wikipedia.org/wiki/Telephone_exchange">rate center</see> of this phone number. Available for only phone numbers from the US and Canada.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("rate_center")]
    public string? RateCenter { get; init; }

    /// <summary>
    /// The latitude of this phone number's location. Available for only phone numbers from the US and Canada.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("latitude")]
    public double? Latitude { get; init; }

    /// <summary>
    /// The longitude of this phone number's location. Available for only phone numbers from the US and Canada.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("longitude")]
    public double? Longitude { get; init; }

    /// <summary>
    /// The two-letter state or province abbreviation of this phone number's location. Available for only phone numbers from the US and Canada.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("region")]
    public string? Region { get; init; }

    /// <summary>
    /// The postal or ZIP code of this phone number's location. Available for only phone numbers from the US and Canada.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("postal_code")]
    public string? PostalCode { get; init; }

    /// <summary>
    /// The <see href="https://en.wikipedia.org/wiki/ISO_3166-1_alpha-2">ISO country code</see> of this phone number.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("iso_country")]
    public string? IsoCountry { get; init; }

    /// <summary>
    /// The type of <see href="https://www.twilio.com/docs/usage/api/address">Address</see> resource the phone number requires. Can be: <c>none</c>, <c>any</c>, <c>local</c>, or <c>foreign</c>. <c>none</c> means no address is required. <c>any</c> means an address is required, but it can be anywhere in the world. <c>local</c> means an address in the phone number's country is required. <c>foreign</c> means an address outside of the phone number's country is required.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("address_requirements")]
    public string? AddressRequirements { get; init; }

    /// <summary>
    /// Whether the phone number is new to the Twilio platform. Can be: <c>true</c> or <c>false</c>.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("beta")]
    public bool? Beta { get; init; }

    /// <summary>
    /// The set of Boolean properties that indicate whether a phone number can receive calls or messages.  Capabilities are: <c>Voice</c>, <c>SMS</c>, and <c>MMS</c> and each capability can be: <c>true</c> or <c>false</c>.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("capabilities")]
    public Capabilities? Capabilities { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
