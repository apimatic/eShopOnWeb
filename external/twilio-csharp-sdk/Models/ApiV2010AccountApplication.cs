using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Twilio.Core.Models;
using Twilio.Core.Validation;
using Twilio.Core.Validation.Attributes;
using Twilio.Models.Enums;

namespace Twilio.Models;

public record ApiV2010AccountApplication
{
    /// <summary>
    /// The SID of the <see href="https://www.twilio.com/docs/iam/api/account">Account</see> that created the Application resource.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("account_sid")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^AC[0-9a-fA-F]{32}$")]
    public string? AccountSid { get; init; }

    /// <summary>
    /// The API version used to start a new TwiML session.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("api_version")]
    public string? ApiVersion { get; init; }

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
    /// The URL we call using a POST method to send message status information to your application.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("message_status_callback")]
    [Format(FormatKind.Uri)]
    public string? MessageStatusCallback { get; init; }

    /// <summary>
    /// The unique string that that we created to identify the Application resource.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("sid")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^AP[0-9a-fA-F]{32}$")]
    public string? Sid { get; init; }

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
    /// The URL we call using a POST method to send status information to your application about SMS messages that refer to the application.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("sms_status_callback")]
    [Format(FormatKind.Uri)]
    public string? SmsStatusCallback { get; init; }

    /// <summary>
    /// The URL we call when the phone number receives an incoming SMS message.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("sms_url")]
    [Format(FormatKind.Uri)]
    public string? SmsUrl { get; init; }

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
    /// The URI of the resource, relative to <c>https://api.twilio.com</c>.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("uri")]
    public string? Uri { get; init; }

    /// <summary>
    /// Whether we look up the caller's caller-ID name from the CNAM database (additional charges apply). Can be: <c>true</c> or <c>false</c>.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("voice_caller_id_lookup")]
    public bool? VoiceCallerIdLookup { get; init; }

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
    /// The HTTP method we use to call <c>voice_url</c>. Can be: <c>GET</c> or <c>POST</c>.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("voice_method")]
    public VoiceMethod? VoiceMethod { get; init; }

    /// <summary>
    /// The URL we call when the phone number assigned to this application receives a call.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("voice_url")]
    [Format(FormatKind.Uri)]
    public string? VoiceUrl { get; init; }

    /// <summary>
    /// Whether to allow other Twilio accounts to dial this applicaton using Dial verb. Can be: <c>true</c> or <c>false</c>.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("public_application_connect_enabled")]
    public bool? PublicApplicationConnectEnabled { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
