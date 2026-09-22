using System.Collections.Generic;
using System.Text.Json.Serialization;
using TwilioSdk.Core.Models;
using TwilioSdk.Core.Validation;
using TwilioSdk.Core.Validation.Attributes;
using TwilioSdk.Models.Enums;

namespace TwilioSdk.Models;

public record LookupResponse
{
    /// <summary>
    /// International dialing prefix of the phone number defined in the E.164 standard.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("calling_country_code")]
    public string? CallingCountryCode { get; init; }

    /// <summary>
    /// The phone number's <see href="https://en.wikipedia.org/wiki/ISO_3166-1_alpha-2">ISO country code</see>.
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
    /// The phone number in <see href="https://en.wikipedia.org/wiki/National_conventions_for_writing_telephone_numbers">national format</see>.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("national_format")]
    public string? NationalFormat { get; init; }

    /// <summary>
    /// Boolean which indicates if the phone number is in a valid range that can be freely assigned by a carrier to a user.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("valid")]
    public bool? Valid { get; init; }

    /// <summary>
    /// Contains reasons why a phone number is invalid. Possible values: TOO_SHORT, TOO_LONG, INVALID_BUT_POSSIBLE, INVALID_COUNTRY_CODE, INVALID_LENGTH, NOT_A_NUMBER.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("validation_errors")]
    public IReadOnlyList<ValidationError>? ValidationErrors { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("caller_name")]
    public CallerNameInfo? CallerName { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("sim_swap")]
    public SimSwapInfo? SimSwap { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("call_forwarding")]
    public CallForwardingInfo? CallForwarding { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("line_type_intelligence")]
    public LineTypeIntelligenceInfo? LineTypeIntelligence { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("line_status")]
    public LineStatusInfo? LineStatus { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("identity_match")]
    public IdentityMatchInfo? IdentityMatch { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("reassigned_number")]
    public ReassignedNumberInfo? ReassignedNumber { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("sms_pumping_risk")]
    public SmsPumpingRiskInfo? SmsPumpingRisk { get; init; }

    /// <summary>
    /// An object that contains information of a mobile phone number quality score. Quality score will return a risk score about the phone number.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("phone_number_quality_score")]
    public object? PhoneNumberQualityScore { get; init; }

    /// <summary>
    /// An object that contains pre fill information. pre_fill will return PII information associated with the phone number like first name, last name, address line, country code, state and postal code.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("pre_fill")]
    public object? PreFill { get; init; }

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
