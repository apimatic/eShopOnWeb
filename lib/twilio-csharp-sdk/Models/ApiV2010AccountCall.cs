using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Twilio.Core.Models;
using Twilio.Models.Enums;

namespace Twilio.Models;

public record ApiV2010AccountCall
{
    /// <summary>
    /// The unique string that we created to identify this Call resource.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("sid")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^CA[0-9a-fA-F]{32}$")]
    public string? Sid { get; init; }

    /// <summary>
    /// The date and time in UTC that this resource was created specified in <see href="https://www.ietf.org/rfc/rfc2822.txt">RFC 2822</see> format.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("date_created")]
    public string? DateCreated { get; init; }

    /// <summary>
    /// The date and time in UTC that this resource was last updated, specified in <see href="https://www.ietf.org/rfc/rfc2822.txt">RFC 2822</see> format.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("date_updated")]
    public string? DateUpdated { get; init; }

    /// <summary>
    /// The SID that identifies the call that created this leg.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("parent_call_sid")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^CA[0-9a-fA-F]{32}$")]
    public string? ParentCallSid { get; init; }

    /// <summary>
    /// The SID of the <see href="https://www.twilio.com/docs/iam/api/account">Account</see> that created this Call resource.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("account_sid")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^AC[0-9a-fA-F]{32}$")]
    public string? AccountSid { get; init; }

    /// <summary>
    /// The phone number, SIP address, Client identifier or SIM SID that received this call. Phone numbers are in <see href="https://www.twilio.com/docs/glossary/what-e164">E.164</see> format (e.g., +16175551212). SIP addresses are formatted as <c>name@company.com</c>. Client identifiers are formatted <c>client:name</c>. SIM SIDs are formatted as <c>sim:sid</c>.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("to")]
    public string? To { get; init; }

    /// <summary>
    /// The phone number, SIP address or Client identifier that received this call. Formatted for display. Non-North American phone numbers are in <see href="https://www.twilio.com/docs/glossary/what-e164">E.164</see> format (e.g., +442071838750).
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("to_formatted")]
    public string? ToFormatted { get; init; }

    /// <summary>
    /// The phone number, SIP address, Client identifier or SIM SID that made this call. Phone numbers are in <see href="https://www.twilio.com/docs/glossary/what-e164">E.164</see> format (e.g., +16175551212). SIP addresses are formatted as <c>name@company.com</c>. Client identifiers are formatted <c>client:name</c>. SIM SIDs are formatted as <c>sim:sid</c>.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("from")]
    public string? From { get; init; }

    /// <summary>
    /// The calling phone number, SIP address, or Client identifier formatted for display. Non-North American phone numbers are in <see href="https://www.twilio.com/docs/glossary/what-e164">E.164</see> format (e.g., +442071838750).
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("from_formatted")]
    public string? FromFormatted { get; init; }

    /// <summary>
    /// If the call was inbound, this is the SID of the IncomingPhoneNumber resource that received the call. If the call was outbound, it is the SID of the OutgoingCallerId resource from which the call was placed.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("phone_number_sid")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^PN[0-9a-fA-F]{32}$")]
    public string? PhoneNumberSid { get; init; }

    /// <summary>
    /// The status of this call. Can be: <c>queued</c>, <c>ringing</c>, <c>in-progress</c>, <c>canceled</c>, <c>completed</c>, <c>failed</c>, <c>busy</c> or <c>no-answer</c>. See <see href="https://www.twilio.com/docs/voice/api/call-resource#call-status-values">Call Status Values</see> below for more information.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("status")]
    public CallEnumStatus? Status { get; init; }

    /// <summary>
    /// The start time of the call, given as UTC in <see href="https://www.php.net/manual/en/class.datetime.php#datetime.constants.rfc2822">RFC 2822</see> format. Empty if the call has not yet been dialed.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("start_time")]
    public string? StartTime { get; init; }

    /// <summary>
    /// The time the call ended, given as UTC in <see href="https://www.php.net/manual/en/class.datetime.php#datetime.constants.rfc2822">RFC 2822</see> format. Empty if the call did not complete successfully.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("end_time")]
    public string? EndTime { get; init; }

    /// <summary>
    /// The length of the call in seconds. This value is empty for busy, failed, unanswered, or ongoing calls.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("duration")]
    public string? Duration { get; init; }

    /// <summary>
    /// The charge for this call, in the currency associated with the account. Populated after the call is completed. May not be immediately available. The price associated with a call only reflects the charge for connectivity.  Charges for other call-related features such as Answering Machine Detection, Text-To-Speech, and SIP REFER are not included in this value.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("price")]
    public string? Price { get; init; }

    /// <summary>
    /// The currency in which <c>Price</c> is measured, in <see href="https://www.iso.org/iso/home/standards/currency_codes.htm">ISO 4127</see> format (e.g., <c>USD</c>, <c>EUR</c>, <c>JPY</c>). Always capitalized for calls.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("price_unit")]
    public string? PriceUnit { get; init; }

    /// <summary>
    /// A string describing the direction of the call. Can be: <c>inbound</c> for inbound calls, <c>outbound-api</c> for calls initiated via the REST API or <c>outbound-dial</c> for calls initiated by a <c>&lt;Dial&gt;</c> verb. Using <see href="https://www.twilio.com/docs/sip-trunking">Elastic SIP Trunking</see>, the values can be <see href="https://www.twilio.com/docs/sip-trunking#termination"><c>trunking-terminating</c></see> for outgoing calls from your communications infrastructure to the PSTN or <see href="https://www.twilio.com/docs/sip-trunking#origination"><c>trunking-originating</c></see> for incoming calls to your communications infrastructure from the PSTN.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("direction")]
    public string? Direction { get; init; }

    /// <summary>
    /// Either <c>human</c> or <c>machine</c> if this call was initiated with answering machine detection. Empty otherwise.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("answered_by")]
    public string? AnsweredBy { get; init; }

    /// <summary>
    /// The API version used to create the call.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("api_version")]
    public string? ApiVersion { get; init; }

    /// <summary>
    /// The forwarding phone number if this call was an incoming call forwarded from another number (depends on carrier supporting forwarding). Otherwise, empty.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("forwarded_from")]
    public string? ForwardedFrom { get; init; }

    /// <summary>
    /// The Group SID associated with this call. If no Group is associated with the call, the field is empty.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("group_sid")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^GP[0-9a-fA-F]{32}$")]
    public string? GroupSid { get; init; }

    /// <summary>
    /// The caller's name if this call was an incoming call to a phone number with caller ID Lookup enabled. Otherwise, empty.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("caller_name")]
    public string? CallerName { get; init; }

    /// <summary>
    /// The wait time in milliseconds before the call is placed.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("queue_time")]
    public string? QueueTime { get; init; }

    /// <summary>
    /// The unique identifier of the trunk resource that was used for this call. The field is empty if the call was not made using a SIP trunk or if the call is not terminated.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("trunk_sid")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^TK[0-9a-fA-F]{32}$")]
    public string? TrunkSid { get; init; }

    /// <summary>
    /// The URI of this resource, relative to <c>https://api.twilio.com</c>.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("uri")]
    public string? Uri { get; init; }

    /// <summary>
    /// A list of subresources available to this call, identified by their URIs relative to <c>https://api.twilio.com</c>.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("subresource_uris")]
    public object? SubresourceUris { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
