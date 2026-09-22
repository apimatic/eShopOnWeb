using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using TwilioSdk.Core.Models;
using TwilioSdk.Models.Enums;

namespace TwilioSdk.Models;

public record ApiV2010AccountConferenceParticipant
{
    /// <summary>
    /// The SID of the <see href="https://www.twilio.com/docs/iam/api/account">Account</see> that created the Participant resource.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("account_sid")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^AC[0-9a-fA-F]{32}$")]
    public string? AccountSid { get; init; }

    /// <summary>
    /// The SID of the <see href="https://www.twilio.com/docs/voice/api/call-resource">Call</see> the Participant resource is associated with.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("call_sid")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^CA[0-9a-fA-F]{32}$")]
    public string? CallSid { get; init; }

    /// <summary>
    /// The user-specified label of this participant, if one was given when the participant was created. This may be used to fetch, update or delete the participant.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("label")]
    public string? Label { get; init; }

    /// <summary>
    /// The SID of the participant who is being <c>coached</c>. The participant being coached is the only participant who can hear the participant who is <c>coaching</c>.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("call_sid_to_coach")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^CA[0-9a-fA-F]{32}$")]
    public string? CallSidToCoach { get; init; }

    /// <summary>
    /// Whether the participant is coaching another call. Can be: <c>true</c> or <c>false</c>. If not present, defaults to <c>false</c> unless <c>call_sid_to_coach</c> is defined. If <c>true</c>, <c>call_sid_to_coach</c> must be defined.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("coaching")]
    public bool? Coaching { get; init; }

    /// <summary>
    /// The SID of the conference the participant is in.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("conference_sid")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^CF[0-9a-fA-F]{32}$")]
    public string? ConferenceSid { get; init; }

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
    /// Whether the conference ends when the participant leaves. Can be: <c>true</c> or <c>false</c> and the default is <c>false</c>. If <c>true</c>, the conference ends and all other participants drop out when the participant leaves.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("end_conference_on_exit")]
    public bool? EndConferenceOnExit { get; init; }

    /// <summary>
    /// Whether the participant is muted. Can be <c>true</c> or <c>false</c>.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("muted")]
    public bool? Muted { get; init; }

    /// <summary>
    /// Whether the participant is on hold. Can be <c>true</c> or <c>false</c>.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("hold")]
    public bool? Hold { get; init; }

    /// <summary>
    /// Whether the conference starts when the participant joins the conference, if it has not already started. Can be: <c>true</c> or <c>false</c> and the default is <c>true</c>. If <c>false</c> and the conference has not started, the participant is muted and hears background music until another participant starts the conference.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("start_conference_on_enter")]
    public bool? StartConferenceOnEnter { get; init; }

    /// <summary>
    /// The status of the participant's call in a session. Can be: <c>queued</c>, <c>connecting</c>, <c>ringing</c>, <c>connected</c>, <c>complete</c>, or <c>failed</c>.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("status")]
    public ParticipantEnumStatus? Status { get; init; }

    /// <summary>
    /// The wait time in milliseconds before participant's call is placed. Only available in the response to a create participant request.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("queue_time")]
    public string? QueueTime { get; init; }

    /// <summary>
    /// The URI of the resource, relative to <c>https://api.twilio.com</c>.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("uri")]
    public string? Uri { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
