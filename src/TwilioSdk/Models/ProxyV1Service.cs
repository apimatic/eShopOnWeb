using System;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using TwilioSdk.Core.Models;
using TwilioSdk.Core.Validation;
using TwilioSdk.Core.Validation.Attributes;
using TwilioSdk.Models.Enums;

namespace TwilioSdk.Models;

public record ProxyV1Service
{
    /// <summary>
    /// The unique string that we created to identify the Service resource.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("sid")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^KS[0-9a-fA-F]{32}$")]
    public string? Sid { get; init; }

    /// <summary>
    /// An application-defined string that uniquely identifies the resource. This value must be 191 characters or fewer in length and be unique. Supports UTF-8 characters. <b>This value should not have PII.</b>
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("unique_name")]
    public string? UniqueName { get; init; }

    /// <summary>
    /// The SID of the <see href="https://www.twilio.com/docs/iam/api/account">Account</see> that created the Service resource.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("account_sid")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^AC[0-9a-fA-F]{32}$")]
    public string? AccountSid { get; init; }

    /// <summary>
    /// The SID of the Chat Service Instance managed by Proxy Service. The Chat Service enables Proxy to forward SMS and channel messages to this chat instance. This is a one-to-one relationship.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("chat_instance_sid")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^IS[0-9a-fA-F]{32}$")]
    public string? ChatInstanceSid { get; init; }

    /// <summary>
    /// The URL we call when the interaction status changes.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("callback_url")]
    [Format(FormatKind.Uri)]
    public string? CallbackUrl { get; init; }

    /// <summary>
    /// The default <c>ttl</c> value for Sessions created in the Service. The TTL (time to live) is measured in seconds after the Session's last create or last Interaction. The default value of <c>0</c> indicates an unlimited Session length. You can override a Session's default TTL value by setting its <c>ttl</c> value.
    /// </summary>
    [JsonPropertyName("default_ttl")]
    public int? DefaultTtl { get; init; } = 0;

    /// <summary>
    /// The preference for Proxy Number selection in the Service instance. Can be: <c>prefer-sticky</c> or <c>avoid-sticky</c>. <c>prefer-sticky</c> means that we will try and select the same Proxy Number for a given participant if they have previous <see href="https://www.twilio.com/docs/proxy/api/session">Sessions</see>, but we will not fail if that Proxy Number cannot be used.  <c>avoid-sticky</c> means that we will try to use different Proxy Numbers as long as that is possible within a given pool rather than try and use a previously assigned number.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("number_selection_behavior")]
    public ServiceEnumNumberSelectionBehavior? NumberSelectionBehavior { get; init; }

    /// <summary>
    /// Where a proxy number must be located relative to the participant identifier. Can be: <c>country</c>, <c>area-code</c>, or <c>extended-area-code</c>. The default value is <c>country</c> and more specific areas than <c>country</c> are only available in North America.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("geo_match_level")]
    public ServiceEnumGeoMatchLevel? GeoMatchLevel { get; init; }

    /// <summary>
    /// The URL we call on each interaction. If we receive a 403 status, we block the interaction; otherwise the interaction continues.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("intercept_callback_url")]
    [Format(FormatKind.Uri)]
    public string? InterceptCallbackUrl { get; init; }

    /// <summary>
    /// The URL we call when an inbound call or SMS action occurs on a closed or non-existent Session. If your server (or a Twilio <see href="https://www.twilio.com/en-us/serverless/functions">function</see>) responds with valid <see href="https://www.twilio.com/docs/voice/twiml">TwiML</see>, we will process it. This means it is possible, for example, to play a message for a call, send an automated text message response, or redirect a call to another Phone Number. See <see href="https://www.twilio.com/docs/proxy/out-session-callback-response-guide">Out-of-Session Callback Response Guide</see> for more information.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("out_of_session_callback_url")]
    [Format(FormatKind.Uri)]
    public string? OutOfSessionCallbackUrl { get; init; }

    /// <summary>
    /// The <see href="https://en.wikipedia.org/wiki/ISO_8601">ISO 8601</see> date and time in GMT when the resource was created.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("date_created")]
    public DateTimeOffset? DateCreated { get; init; }

    /// <summary>
    /// The <see href="https://en.wikipedia.org/wiki/ISO_8601">ISO 8601</see> date and time in GMT when the resource was last updated.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("date_updated")]
    public DateTimeOffset? DateUpdated { get; init; }

    /// <summary>
    /// The absolute URL of the Service resource.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("url")]
    [Format(FormatKind.Uri)]
    public string? Url { get; init; }

    /// <summary>
    /// The URLs of resources related to the Service.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("links")]
    public object? Links { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
