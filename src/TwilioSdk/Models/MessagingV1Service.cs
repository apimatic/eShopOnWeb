using System;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using TwilioSdk.Core.Models;
using TwilioSdk.Core.Validation;
using TwilioSdk.Core.Validation.Attributes;
using TwilioSdk.Models.Enums;

namespace TwilioSdk.Models;

public record MessagingV1Service
{
    /// <summary>
    /// The unique string that we created to identify the Service resource.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("sid")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^MG[0-9a-fA-F]{32}$")]
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
    /// The string that you assigned to describe the resource.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("friendly_name")]
    public string? FriendlyName { get; init; }

    /// <summary>
    /// The date and time in GMT when the resource was created specified in <see href="https://en.wikipedia.org/wiki/ISO_8601">ISO 8601</see> format.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("date_created")]
    public DateTimeOffset? DateCreated { get; init; }

    /// <summary>
    /// The date and time in GMT when the resource was last updated specified in <see href="https://en.wikipedia.org/wiki/ISO_8601">ISO 8601</see> format.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("date_updated")]
    public DateTimeOffset? DateUpdated { get; init; }

    /// <summary>
    /// The URL we call using <c>inbound_method</c> when a message is received by any phone number or short code in the Service. When this property is <c>null</c>, receiving inbound messages is disabled. All messages sent to the Twilio phone number or short code will not be logged and received on the Account. If the <c>use_inbound_webhook_on_number</c> field is enabled then the webhook url defined on the phone number will override the <c>inbound_request_url</c> defined for the Messaging Service.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("inbound_request_url")]
    [Format(FormatKind.Uri)]
    public string? InboundRequestUrl { get; init; }

    /// <summary>
    /// The HTTP method we use to call <c>inbound_request_url</c>. Can be <c>GET</c> or <c>POST</c>.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("inbound_method")]
    public AmdStatusCallbackMethod? InboundMethod { get; init; }

    /// <summary>
    /// The URL that we call using <c>fallback_method</c> if an error occurs while retrieving or executing the TwiML from the Inbound Request URL. If the <c>use_inbound_webhook_on_number</c> field is enabled then the webhook url defined on the phone number will override the <c>fallback_url</c> defined for the Messaging Service.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("fallback_url")]
    [Format(FormatKind.Uri)]
    public string? FallbackUrl { get; init; }

    /// <summary>
    /// The HTTP method we use to call <c>fallback_url</c>. Can be: <c>GET</c> or <c>POST</c>.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("fallback_method")]
    public AmdStatusCallbackMethod? FallbackMethod { get; init; }

    /// <summary>
    /// The URL we call to <see href="https://www.twilio.com/docs/sms/api/message-resource#message-status-values">pass status updates</see> about message delivery.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("status_callback")]
    [Format(FormatKind.Uri)]
    public string? StatusCallback { get; init; }

    /// <summary>
    /// Whether to enable <see href="https://www.twilio.com/docs/messaging/services#sticky-sender">Sticky Sender</see> on the Service instance.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("sticky_sender")]
    public bool? StickySender { get; init; }

    /// <summary>
    /// Whether to enable the <see href="https://www.twilio.com/docs/messaging/services#mms-converter">MMS Converter</see> for messages sent through the Service instance.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("mms_converter")]
    public bool? MmsConverter { get; init; }

    /// <summary>
    /// Whether to enable <see href="https://www.twilio.com/docs/messaging/services#smart-encoding">Smart Encoding</see> for messages sent through the Service instance.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("smart_encoding")]
    public bool? SmartEncoding { get; init; }

    /// <summary>
    /// Reserved.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("scan_message_content")]
    public ServiceEnumScanMessageContent? ScanMessageContent { get; init; }

    /// <summary>
    /// [OBSOLETE] Former feature used to fallback to long code sender after certain short code message failures.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("fallback_to_long_code")]
    public bool? FallbackToLongCode { get; init; }

    /// <summary>
    /// Whether to enable <see href="https://www.twilio.com/docs/messaging/services#area-code-geomatch">Area Code Geomatch</see> on the Service Instance.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("area_code_geomatch")]
    public bool? AreaCodeGeomatch { get; init; }

    /// <summary>
    /// Reserved.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("synchronous_validation")]
    public bool? SynchronousValidation { get; init; }

    /// <summary>
    /// How long, in seconds, messages sent from the Service are valid. Can be an integer from <c>1</c> to <c>36,000</c>. Default value is <c>36,000</c>.
    /// </summary>
    [JsonPropertyName("validity_period")]
    public int? ValidityPeriod { get; init; } = 0;

    /// <summary>
    /// The absolute URL of the Service resource.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("url")]
    [Format(FormatKind.Uri)]
    public string? Url { get; init; }

    /// <summary>
    /// The absolute URLs of related resources.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("links")]
    public object? Links { get; init; }

    /// <summary>
    /// A string that describes the scenario in which the Messaging Service will be used. Possible values are <c>notifications</c>, <c>marketing</c>, <c>verification</c>, <c>discussion</c>, <c>poll</c>, <c>undeclared</c>.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("usecase")]
    public string? Usecase { get; init; }

    /// <summary>
    /// Whether US A2P campaign is registered for this Service.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("us_app_to_person_registered")]
    public bool? UsAppToPersonRegistered { get; init; }

    /// <summary>
    /// A boolean value that indicates either the webhook url configured on the phone number will be used or <c>inbound_request_url</c>/<c>fallback_url</c> url will be called when a message is received from the phone number. If this field is enabled then the webhook url defined on the phone number will override the <c>inbound_request_url</c>/<c>fallback_url</c> defined for the Messaging Service.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("use_inbound_webhook_on_number")]
    public bool? UseInboundWebhookOnNumber { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
