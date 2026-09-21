using System;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using TwilioSdk.Core.Models;
using TwilioSdk.Core.Validation;
using TwilioSdk.Core.Validation.Attributes;

namespace TwilioSdk.Models;

public record VerifyV2Service
{
    /// <summary>
    /// The unique string that we created to identify the Service resource.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("sid")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^VA[0-9a-fA-F]{32}$")]
    public string? Sid { get; init; }

    /// <summary>
    /// The SID of the <see href="https://www.twilio.com/docs/iam/api/account">Account</see> that created the Service resource.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("account_sid")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^AC[0-9a-fA-F]{32}$")]
    public string? AccountSid { get; init; }

    /// <summary>
    /// The name that appears in the body of your verification messages. It can be up to 30 characters long and can include letters, numbers, spaces, dashes, underscores. Phone numbers, special characters or links are NOT allowed. It cannot contain more than 4 (consecutive or non-consecutive) digits. <b>This value should not contain PII.</b>
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("friendly_name")]
    public string? FriendlyName { get; init; }

    /// <summary>
    /// The length of the verification code to generate.
    /// </summary>
    [JsonPropertyName("code_length")]
    public int? CodeLength { get; init; } = 0;

    /// <summary>
    /// Whether to perform a lookup with each verification started and return info about the phone number.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("lookup_enabled")]
    public bool? LookupEnabled { get; init; }

    /// <summary>
    /// Whether to pass PSD2 transaction parameters when starting a verification.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("psd2_enabled")]
    public bool? Psd2Enabled { get; init; }

    /// <summary>
    /// Whether to skip sending SMS verifications to landlines. Requires <c>lookup_enabled</c>.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("skip_sms_to_landlines")]
    public bool? SkipSmsToLandlines { get; init; }

    /// <summary>
    /// Whether to ask the user to press a number before delivering the verify code in a phone call.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("dtmf_input_required")]
    public bool? DtmfInputRequired { get; init; }

    /// <summary>
    /// The name of an alternative text-to-speech service to use in phone calls. Applies only to TTS languages.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("tts_name")]
    public string? TtsName { get; init; }

    /// <summary>
    /// Whether to add a security warning at the end of an SMS verification body. Disabled by default and applies only to SMS. Example SMS body: <c>Your AppName verification code is: 1234. Don’t share this code with anyone; our employees will never ask for the code</c>
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("do_not_share_warning_enabled")]
    public bool? DoNotShareWarningEnabled { get; init; }

    /// <summary>
    /// Whether to allow sending verifications with a custom code instead of a randomly generated one.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("custom_code_enabled")]
    public bool? CustomCodeEnabled { get; init; }

    /// <summary>
    /// Configurations for the Push factors (channel) created under this Service.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("push")]
    public object? Push { get; init; }

    /// <summary>
    /// Configurations for the TOTP factors (channel) created under this Service.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("totp")]
    public object? Totp { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("default_template_sid")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^HJ[0-9a-fA-F]{32}$")]
    public string? DefaultTemplateSid { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("whatsapp")]
    public object? Whatsapp { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("passkeys")]
    public object? Passkeys { get; init; }

    /// <summary>
    /// Whether to allow verifications from the service to reach the stream-events sinks if configured
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("verify_event_subscription_enabled")]
    public bool? VerifyEventSubscriptionEnabled { get; init; }

    /// <summary>
    /// The date and time in GMT when the resource was created specified in <see href="https://www.ietf.org/rfc/rfc2822.txt">RFC 2822</see> format.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("date_created")]
    public DateTimeOffset? DateCreated { get; init; }

    /// <summary>
    /// The date and time in GMT when the resource was last updated specified in <see href="https://www.ietf.org/rfc/rfc2822.txt">RFC 2822</see> format.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("date_updated")]
    public DateTimeOffset? DateUpdated { get; init; }

    /// <summary>
    /// The absolute URL of the resource.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("url")]
    [Format(FormatKind.Uri)]
    public string? Url { get; init; }

    /// <summary>
    /// The URLs of related resources.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("links")]
    public object? Links { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
