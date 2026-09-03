using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using TwilioSdk.Core.Models;
using TwilioSdk.Core.Validation;
using TwilioSdk.Core.Validation.Attributes;
using TwilioSdk.Models.Enums;

namespace TwilioSdk.Models;

public record ApiV2010AccountAddressDependentPhoneNumber
{
    /// <summary>
    /// The unique string that that we created to identify the DependentPhoneNumber resource.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("sid")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^PN[0-9a-fA-F]{32}$")]
    public string? Sid { get; init; }

    /// <summary>
    /// The SID of the <see href="https://www.twilio.com/docs/iam/api/account">Account</see> that created the DependentPhoneNumber resource.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("account_sid")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^AC[0-9a-fA-F]{32}$")]
    public string? AccountSid { get; init; }

    /// <summary>
    /// The string that you assigned to describe the resource.
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
    /// The URL we call when the phone number receives a call. The <c>voice_url</c> will not be used if a <c>voice_application_sid</c> or a <c>trunk_sid</c> is set.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("voice_url")]
    [Format(FormatKind.Uri)]
    public string? VoiceUrl { get; init; }

    /// <summary>
    /// The HTTP method we use to call <c>voice_url</c>. Can be: <c>GET</c> or <c>POST</c>.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("voice_method")]
    public VoiceMethod? VoiceMethod { get; init; }

    /// <summary>
    /// The HTTP method we use to call <c>voice_fallback_url</c>. Can be: <c>GET</c> or <c>POST</c>.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("voice_fallback_method")]
    public VoiceFallbackMethod? VoiceFallbackMethod { get; init; }

    /// <summary>
    /// The URL that we call when an error occurs retrieving or executing the TwiML requested by <c>url</c>.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("voice_fallback_url")]
    [Format(FormatKind.Uri)]
    public string? VoiceFallbackUrl { get; init; }

    /// <summary>
    /// Whether we look up the caller's caller-ID name from the CNAM database. Can be: <c>true</c> or <c>false</c>. Caller ID lookups can cost $0.01 each.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("voice_caller_id_lookup")]
    public bool? VoiceCallerIdLookup { get; init; }

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
    /// The HTTP method we use to call <c>sms_fallback_url</c>. Can be: <c>GET</c> or <c>POST</c>.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("sms_fallback_method")]
    public SmsFallbackMethod? SmsFallbackMethod { get; init; }

    /// <summary>
    /// The URL that we call when an error occurs while retrieving or executing the TwiML from <c>sms_url</c>.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("sms_fallback_url")]
    [Format(FormatKind.Uri)]
    public string? SmsFallbackUrl { get; init; }

    /// <summary>
    /// The HTTP method we use to call <c>sms_url</c>. Can be: <c>GET</c> or <c>POST</c>.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("sms_method")]
    public SmsMethod? SmsMethod { get; init; }

    /// <summary>
    /// The URL we call when the phone number receives an incoming SMS message.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("sms_url")]
    [Format(FormatKind.Uri)]
    public string? SmsUrl { get; init; }

    /// <summary>
    /// Whether the phone number requires an <see href="https://www.twilio.com/docs/usage/api/address">Address</see> registered with Twilio. Can be: <c>none</c>, <c>any</c>, <c>local</c>, or <c>foreign</c>.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("address_requirements")]
    public DependentPhoneNumberEnumAddressRequirement? AddressRequirements { get; init; }

    /// <summary>
    /// The set of Boolean properties that indicates whether a phone number can receive calls or messages.  Capabilities are  <c>Voice</c>, <c>SMS</c>, and <c>MMS</c> and each capability can be: <c>true</c> or <c>false</c>.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("capabilities")]
    public object? Capabilities { get; init; }

    /// <summary>
    /// The URL we call using the <c>status_callback_method</c> to send status information to your application.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("status_callback")]
    [Format(FormatKind.Uri)]
    public string? StatusCallback { get; init; }

    /// <summary>
    /// The HTTP method we use to call <c>status_callback</c>. Can be: <c>GET</c> or <c>POST</c>.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("status_callback_method")]
    public StatusCallbackMethod? StatusCallbackMethod { get; init; }

    /// <summary>
    /// The API version used to start a new TwiML session.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("api_version")]
    public string? ApiVersion { get; init; }

    /// <summary>
    /// The SID of the application that handles SMS messages sent to the phone number. If an <c>sms_application_sid</c> is present, we ignore all <c>sms_*_url</c> values and use those of the application.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("sms_application_sid")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^AP[0-9a-fA-F]{32}$")]
    public string? SmsApplicationSid { get; init; }

    /// <summary>
    /// The SID of the application that handles calls to the phone number. If a <c>voice_application_sid</c> is present, we ignore all of the voice urls and use those set on the application. Setting a <c>voice_application_sid</c> will automatically delete your <c>trunk_sid</c> and vice versa.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("voice_application_sid")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^AP[0-9a-fA-F]{32}$")]
    public string? VoiceApplicationSid { get; init; }

    /// <summary>
    /// The SID of the Trunk that handles calls to the phone number. If a <c>trunk_sid</c> is present, we ignore all of the voice urls and voice applications and use those set on the Trunk. Setting a <c>trunk_sid</c> will automatically delete your <c>voice_application_sid</c> and vice versa.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("trunk_sid")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^TK[0-9a-fA-F]{32}$")]
    public string? TrunkSid { get; init; }

    /// <summary>
    /// Whether the phone number is enabled for emergency calling.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("emergency_status")]
    public DependentPhoneNumberEnumEmergencyStatus? EmergencyStatus { get; init; }

    /// <summary>
    /// The SID of the emergency address configuration that we use for emergency calling from the phone number.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("emergency_address_sid")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^AD[0-9a-fA-F]{32}$")]
    public string? EmergencyAddressSid { get; init; }

    /// <summary>
    /// The URI of the resource, relative to <c>https://api.twilio.com</c>.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("uri")]
    public string? Uri { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
