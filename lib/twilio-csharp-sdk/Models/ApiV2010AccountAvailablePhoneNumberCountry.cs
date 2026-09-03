using System.Text.Json.Serialization;
using Twilio.Core.Models;
using Twilio.Core.Validation;
using Twilio.Core.Validation.Attributes;

namespace Twilio.Models;

public record ApiV2010AccountAvailablePhoneNumberCountry
{
    /// <summary>
    /// The <see href="https://en.wikipedia.org/wiki/ISO_3166-1_alpha-2">ISO-3166-1</see> country code of the country.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("country_code")]
    public string? CountryCode { get; init; }

    /// <summary>
    /// The name of the country.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("country")]
    public string? Country { get; init; }

    /// <summary>
    /// The URI of the Country resource, relative to <c>https://api.twilio.com</c>.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("uri")]
    [Format(FormatKind.Uri)]
    public string? Uri { get; init; }

    /// <summary>
    /// Whether all phone numbers available in the country are new to the Twilio platform. <c>true</c> if they are and <c>false</c> if all numbers are not in the Twilio Phone Number Beta program.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("beta")]
    public bool? Beta { get; init; }

    /// <summary>
    /// A list of related AvailablePhoneNumber resources identified by their URIs relative to <c>https://api.twilio.com</c>.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("subresource_uris")]
    public object? SubresourceUris { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
