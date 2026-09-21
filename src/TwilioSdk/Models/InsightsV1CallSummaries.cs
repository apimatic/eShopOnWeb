using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using TwilioSdk.Core.Models;
using TwilioSdk.Core.Validation;
using TwilioSdk.Core.Validation.Attributes;
using TwilioSdk.Models.Enums;

namespace TwilioSdk.Models;

public record InsightsV1CallSummaries
{
    /// <summary>
    /// The unique SID identifier of the Account.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("account_sid")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^AC[0-9a-fA-F]{32}$")]
    public string? AccountSid { get; init; }

    /// <summary>
    /// The unique SID identifier of the Call.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("call_sid")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^CA[0-9a-fA-F]{32}$")]
    public string? CallSid { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("answered_by")]
    public CallSummariesEnumAnsweredBy? AnsweredBy { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("call_type")]
    public CallSummariesEnumCallType? CallType { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("call_state")]
    public CallSummariesEnumCallState? CallState { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("processing_state")]
    public CallSummariesEnumProcessingState? ProcessingState { get; init; }

    /// <summary>
    /// The time at which the Call was created, given in ISO 8601 format. Can be different from <c>start_time</c> in the event of queueing due to CPS
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("created_time")]
    public DateTimeOffset? CreatedTime { get; init; }

    /// <summary>
    /// The time at which the Call was started, given in ISO 8601 format.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("start_time")]
    public DateTimeOffset? StartTime { get; init; }

    /// <summary>
    /// The time at which the Call was ended, given in ISO 8601 format.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("end_time")]
    public DateTimeOffset? EndTime { get; init; }

    /// <summary>
    /// Duration between when the call was initiated and the call was ended
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("duration")]
    public int? Duration { get; init; }

    /// <summary>
    /// Duration between when the call was answered and when it ended
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("connect_duration")]
    public int? ConnectDuration { get; init; }

    /// <summary>
    /// <c>object</c> The calling party. See <see href="https://www.twilio.com/docs/voice/voice-insights/api/call/details-call-summary#tofrom-object">Details: Call Summary</see> for the object properties.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("from")]
    public object? From { get; init; }

    /// <summary>
    /// <c>object</c> The called party. See <see href="https://www.twilio.com/docs/voice/voice-insights/api/call/details-call-summary#tofrom-object">Details: Call Summary</see> for the object properties.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("to")]
    public object? To { get; init; }

    /// <summary>
    /// <c>object</c> Contains metrics and properties for the Twilio media gateway of a PSTN call. See <see href="https://www.twilio.com/docs/voice/voice-insights/api/call/details-call-summary#edges-and-their-properties">Details: Call Summary</see> for the object properties.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("carrier_edge")]
    public object? CarrierEdge { get; init; }

    /// <summary>
    /// <c>object</c> Contains metrics and properties for the Twilio media gateway of a Client call. See <see href="https://www.twilio.com/docs/voice/voice-insights/api/call/details-call-summary#edges-and-their-properties">Details: Call Summary</see> for the object properties.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("client_edge")]
    public object? ClientEdge { get; init; }

    /// <summary>
    /// <c>object</c> Contains metrics and properties for the SDK sensor library for Client calls. See <see href="https://www.twilio.com/docs/voice/voice-insights/api/call/details-call-summary#edges-and-their-properties">Details: Call Summary</see> for the object properties.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("sdk_edge")]
    public object? SdkEdge { get; init; }

    /// <summary>
    /// <c>object</c> Contains metrics and properties for the Twilio media gateway of a SIP Interface or Trunking call. See <see href="https://www.twilio.com/docs/voice/voice-insights/api/call/details-call-summary#edges-and-their-properties">Details: Call Summary</see> for the object properties.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("sip_edge")]
    public object? SipEdge { get; init; }

    /// <summary>
    /// Tags applied to calls by Voice Insights analysis indicating a condition that could result in subjective degradation of the call quality.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("tags")]
    public IReadOnlyList<string?>? Tags { get; init; }

    /// <summary>
    /// The URL of this resource.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("url")]
    [Format(FormatKind.Uri)]
    public string? Url { get; init; }

    /// <summary>
    /// <c>object</c> Attributes capturing call-flow-specific details. See <see href="https://www.twilio.com/docs/voice/voice-insights/api/call/details-call-summary#attributes-object">Details: Call Summary</see> for the object properties.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("attributes")]
    public object? Attributes { get; init; }

    /// <summary>
    /// <c>object</c> Contains edge-agnostic call-level details. See <see href="https://www.twilio.com/docs/voice/voice-insights/api/call/details-call-summary#properties-object">Details: Call Summary</see> for the object properties.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("properties")]
    public object? Properties { get; init; }

    /// <summary>
    /// <c>object</c> Contains trusted communications details including Branded Call and verified caller ID. See <see href="https://www.twilio.com/docs/voice/voice-insights/api/call/details-call-summary#trust-object">Details: Call Summary</see> for the object properties.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("trust")]
    public object? Trust { get; init; }

    /// <summary>
    /// <c>object</c> Programmatically labeled annotations for the Call. Developers can update the Call Summary records with Annotation during or after a Call. Annotations can be updated as long as the Call Summary record is addressable via the API. See <see href="https://www.twilio.com/docs/voice/voice-insights/api/call/details-call-summary#annotation-object">Details: Call Summary</see> for the object properties.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("annotation")]
    public object? Annotation { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
