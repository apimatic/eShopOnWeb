using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using TwilioSdk.Core.Models;
using TwilioSdk.Core.Validation;
using TwilioSdk.Core.Validation.Attributes;
using TwilioSdk.Models.Enums;

namespace TwilioSdk.Models;

public record ApiV2010AccountSipSipDomain
{
    /// <summary>
    /// The SID of the <see href="https://www.twilio.com/docs/iam/api/account">Account</see> that created the SipDomain resource.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("account_sid")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^AC[0-9a-fA-F]{32}$")]
    public string? AccountSid { get; init; }

    /// <summary>
    /// The API version used to process the call.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("api_version")]
    public string? ApiVersion { get; init; }

    /// <summary>
    /// The types of authentication you have mapped to your domain. Can be: <c>IP_ACL</c> and <c>CREDENTIAL_LIST</c>. If you have both defined for your domain, both will be returned in a comma delimited string. If <c>auth_type</c> is not defined, the domain will not be able to receive any traffic.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("auth_type")]
    public string? AuthType { get; init; }

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
    /// The unique address you reserve on Twilio to which you route your SIP traffic. Domain names can contain letters, digits, and "-" and must end with <c>sip.twilio.com</c>.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("domain_name")]
    public string? DomainName { get; init; }

    /// <summary>
    /// The string that you assigned to describe the resource.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("friendly_name")]
    public string? FriendlyName { get; init; }

    /// <summary>
    /// The unique string that that we created to identify the SipDomain resource.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("sid")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^SD[0-9a-fA-F]{32}$")]
    public string? Sid { get; init; }

    /// <summary>
    /// The URI of the resource, relative to <c>https://api.twilio.com</c>.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("uri")]
    public string? Uri { get; init; }

    /// <summary>
    /// The HTTP method we use to call <c>voice_fallback_url</c>. Can be: <c>GET</c> or <c>POST</c>.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("voice_fallback_method")]
    public VoiceFallbackMethod? VoiceFallbackMethod { get; init; }

    /// <summary>
    /// The URL that we call when an error occurs while retrieving or executing the TwiML requested from <c>voice_url</c>.
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
    /// The HTTP method we use to call <c>voice_status_callback_url</c>. Either <c>GET</c> or <c>POST</c>.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("voice_status_callback_method")]
    public VoiceStatusCallbackMethod? VoiceStatusCallbackMethod { get; init; }

    /// <summary>
    /// The URL that we call to pass status parameters (such as call ended) to your application.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("voice_status_callback_url")]
    [Format(FormatKind.Uri)]
    public string? VoiceStatusCallbackUrl { get; init; }

    /// <summary>
    /// The URL we call using the <c>voice_method</c> when the domain receives a call.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("voice_url")]
    [Format(FormatKind.Uri)]
    public string? VoiceUrl { get; init; }

    /// <summary>
    /// A list of mapping resources associated with the SIP Domain resource identified by their relative URIs.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("subresource_uris")]
    public object? SubresourceUris { get; init; }

    /// <summary>
    /// Whether to allow SIP Endpoints to register with the domain to receive calls.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("sip_registration")]
    public bool? SipRegistration { get; init; }

    /// <summary>
    /// Whether emergency calling is enabled for the domain. If enabled, allows emergency calls on the domain from phone numbers with validated addresses.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("emergency_calling_enabled")]
    public bool? EmergencyCallingEnabled { get; init; }

    /// <summary>
    /// Whether secure SIP is enabled for the domain. If enabled, TLS will be enforced and SRTP will be negotiated on all incoming calls to this sip domain.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("secure")]
    public bool? Secure { get; init; }

    /// <summary>
    /// The SID of the BYOC Trunk(Bring Your Own Carrier) resource that the Sip Domain will be associated with.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("byoc_trunk_sid")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^BY[0-9a-fA-F]{32}$")]
    public string? ByocTrunkSid { get; init; }

    /// <summary>
    /// Whether an emergency caller sid is configured for the domain. If present, this phone number will be used as the callback for the emergency call.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("emergency_caller_sid")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^PN[0-9a-fA-F]{32}$")]
    public string? EmergencyCallerSid { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
