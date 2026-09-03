using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Twilio.Core.Models;
using Twilio.Models.Enums;

namespace Twilio.Models;

public record ApiV2010AccountMessage
{
    /// <summary>
    /// The text content of the message
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("body")]
    public string? Body { get; init; }

    /// <summary>
    /// The number of segments that make up the complete message. SMS message bodies that exceed the <see href="https://www.twilio.com/docs/glossary/what-sms-character-limit">character limit</see> are segmented and charged as multiple messages. Note: For messages sent via a Messaging Service, <c>num_segments</c> is initially <c>0</c>, since a sender hasn't yet been assigned.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("num_segments")]
    public string? NumSegments { get; init; }

    /// <summary>
    /// The direction of the message. Can be: <c>inbound</c> for incoming messages, <c>outbound-api</c> for messages created by the REST API, <c>outbound-call</c> for messages created during a call, or <c>outbound-reply</c> for messages created in response to an incoming message.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("direction")]
    public MessageEnumDirection? Direction { get; init; }

    /// <summary>
    /// The sender's phone number (in <see href="https://en.wikipedia.org/wiki/E.164">E.164</see> format), <see href="https://www.twilio.com/docs/sms/quickstart">alphanumeric sender ID</see>, <see href="https://www.twilio.com/docs/iot/wireless/programmable-wireless-send-machine-machine-sms-commands">Wireless SIM</see>, <see href="https://www.twilio.com/en-us/messaging/channels/sms/short-codes">short code</see>, or  <see href="https://www.twilio.com/docs/messaging/channels">channel address</see> (e.g., <c>whatsapp:+15554449999</c>). For incoming messages, this is the number or channel address of the sender. For outgoing messages, this value is a Twilio phone number, alphanumeric sender ID, short code, or channel address from which the message is sent.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("from")]
    public string? From { get; init; }

    /// <summary>
    /// The recipient's phone number (in <see href="https://en.wikipedia.org/wiki/E.164">E.164</see> format) or <see href="https://www.twilio.com/docs/messaging/channels">channel address</see> (e.g. <c>whatsapp:+15552229999</c>)
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("to")]
    public string? To { get; init; }

    /// <summary>
    /// The <see href="https://datatracker.ietf.org/doc/html/rfc2822#section-3.3">RFC 2822</see> timestamp (in GMT) of when the Message resource was last updated
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("date_updated")]
    public string? DateUpdated { get; init; }

    /// <summary>
    /// The amount billed for the message in the currency specified by <c>price_unit</c>. The <c>price</c> is populated after the message has been sent/received, and may not be immediately availalble. View the <see href="https://www.twilio.com/en-us/pricing">Pricing page</see> for more details.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("price")]
    public string? Price { get; init; }

    /// <summary>
    /// The description of the <c>error_code</c> if the Message <c>status</c> is <c>failed</c> or <c>undelivered</c>. If no error was encountered, the value is <c>null</c>. The value returned in this field for a specific error cause is subject to change as Twilio improves errors. Users should not use the <c>error_code</c> and <c>error_message</c> fields programmatically.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("error_message")]
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// The URI of the Message resource, relative to <c>https://api.twilio.com</c>.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("uri")]
    public string? Uri { get; init; }

    /// <summary>
    /// The SID of the <see href="https://www.twilio.com/docs/iam/api/account">Account</see> associated with the Message resource
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("account_sid")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^AC[0-9a-fA-F]{32}$")]
    public string? AccountSid { get; init; }

    /// <summary>
    /// The number of media files associated with the Message resource.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("num_media")]
    public string? NumMedia { get; init; }

    /// <summary>
    /// The status of the Message. Possible values: <c>accepted</c>, <c>scheduled</c>, <c>canceled</c>, <c>queued</c>, <c>sending</c>, <c>sent</c>, <c>failed</c>, <c>delivered</c>, <c>undelivered</c>, <c>receiving</c>, <c>received</c>, or <c>read</c> (WhatsApp only). For more information, See <see href="https://www.twilio.com/docs/sms/api/message-resource#message-status-values">detailed descriptions</see>.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("status")]
    public MessageEnumStatus? Status { get; init; }

    /// <summary>
    /// The SID of the <see href="https://www.twilio.com/docs/messaging/api/service-resource">Messaging Service</see> associated with the Message resource. A unique default value is assigned if a Messaging Service is not used.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("messaging_service_sid")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^MG[0-9a-fA-F]{32}$")]
    public string? MessagingServiceSid { get; init; }

    /// <summary>
    /// The unique, Twilio-provided string that identifies the Message resource.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("sid")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^(SM|MM)[0-9a-fA-F]{32}$")]
    public string? Sid { get; init; }

    /// <summary>
    /// The <see href="https://datatracker.ietf.org/doc/html/rfc2822#section-3.3">RFC 2822</see> timestamp (in GMT) of when the Message was sent. For an outgoing message, this is when Twilio sent the message. For an incoming message, this is when Twilio sent the HTTP request to your incoming message webhook URL.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("date_sent")]
    public string? DateSent { get; init; }

    /// <summary>
    /// The <see href="https://datatracker.ietf.org/doc/html/rfc2822#section-3.3">RFC 2822</see> timestamp (in GMT) of when the Message resource was created
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("date_created")]
    public string? DateCreated { get; init; }

    /// <summary>
    /// The <see href="https://www.twilio.com/docs/api/errors">error code</see> returned if the Message <c>status</c> is <c>failed</c> or <c>undelivered</c>. If no error was encountered, the value is <c>null</c>. The value returned in this field for a specific error cause is subject to change as Twilio improves errors. Users should not use the <c>error_code</c> and <c>error_message</c> fields programmatically.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("error_code")]
    public int? ErrorCode { get; init; }

    /// <summary>
    /// The currency in which <c>price</c> is measured, in <see href="https://www.iso.org/iso/home/standards/currency_codes.htm">ISO 4127</see> format (e.g. <c>usd</c>, <c>eur</c>, <c>jpy</c>).
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("price_unit")]
    public string? PriceUnit { get; init; }

    /// <summary>
    /// The API version used to process the Message
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("api_version")]
    public string? ApiVersion { get; init; }

    /// <summary>
    /// A list of related resources identified by their URIs relative to <c>https://api.twilio.com</c>
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("subresource_uris")]
    public object? SubresourceUris { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
