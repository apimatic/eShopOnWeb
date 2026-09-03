using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Twilio.Core.Models;
using Twilio.Core.Validation;
using Twilio.Core.Validation.Attributes;
using Twilio.Models.Enums;

namespace Twilio.Models;

public record NumbersV2RegulatoryComplianceRegulation
{
    /// <summary>
    /// The unique string that identifies the Regulation resource.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("sid")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^RN[0-9a-fA-F]{32}$")]
    public string? Sid { get; init; }

    /// <summary>
    /// A human-readable description that is assigned to describe the Regulation resource. Examples can include Germany: Mobile - Business.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("friendly_name")]
    public string? FriendlyName { get; init; }

    /// <summary>
    /// The ISO country code of the phone number's country.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("iso_country")]
    public string? IsoCountry { get; init; }

    /// <summary>
    /// The type of phone number restricted by the regulatory requirement. For example, Germany mobile phone numbers provisioned by businesses require a business name with commercial register proof from the Handelsregisterauszug and a proof of address from Handelsregisterauszug or a trade license by Gewerbeanmeldung.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("number_type")]
    public string? NumberType { get; init; }

    /// <summary>
    /// The type of End User the regulation requires - can be <c>individual</c> or <c>business</c>.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("end_user_type")]
    public RegulationEnumEndUserType? EndUserType { get; init; }

    /// <summary>
    /// The SID of an object that holds the regulatory information of the phone number country, phone number type, and end user type.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("requirements")]
    public object? Requirements { get; init; }

    /// <summary>
    /// The absolute URL of the Regulation resource.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("url")]
    [Format(FormatKind.Uri)]
    public string? Url { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
