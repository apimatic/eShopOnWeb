using System.Text.Json.Serialization;
using Twilio.Core.Models;
using Twilio.Core.Validation;
using Twilio.Core.Validation.Attributes;

namespace Twilio.Models;

public record LookupsV1PhoneNumber
{
    /// <summary>
    /// The name of the phone number's owner. If <c>null</c>, that information was not available.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("caller_name")]
    public object? CallerName { get; init; }

    /// <summary>
    /// The <see href="https://en.wikipedia.org/wiki/ISO_3166-1_alpha-2">ISO country code</see> for the phone number.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("country_code")]
    public string? CountryCode { get; init; }

    /// <summary>
    /// The phone number in <see href="https://www.twilio.com/docs/glossary/what-e164">E.164</see> format, which consists of a + followed by the country code and subscriber number.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("phone_number")]
    public string? PhoneNumber { get; init; }

    /// <summary>
    /// The phone number, in national format.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("national_format")]
    public string? NationalFormat { get; init; }

    /// <summary>
    /// The telecom company that provides the phone number.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("carrier")]
    public object? Carrier { get; init; }

    /// <summary>
    /// A JSON string with the results of the Add-ons you specified in the <c>add_ons</c> parameters. For the format of the object, see <see href="https://www.twilio.com/docs/add-ons">Using Add-ons</see>.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("add_ons")]
    public object? AddOns { get; init; }

    /// <summary>
    /// The absolute URL of the resource.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("url")]
    [Format(FormatKind.Uri)]
    public string? Url { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
