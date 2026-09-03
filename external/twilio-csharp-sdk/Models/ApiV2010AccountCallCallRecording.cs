using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Twilio.Core.Models;
using Twilio.Models.Enums;

namespace Twilio.Models;

public record ApiV2010AccountCallCallRecording
{
    /// <summary>
    /// The SID of the <see href="https://www.twilio.com/docs/iam/api/account">Account</see> that created the Recording resource.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("account_sid")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^AC[0-9a-fA-F]{32}$")]
    public string? AccountSid { get; init; }

    /// <summary>
    /// The API version used to make the recording.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("api_version")]
    public string? ApiVersion { get; init; }

    /// <summary>
    /// The SID of the <see href="https://www.twilio.com/docs/voice/api/call-resource">Call</see> the Recording resource is associated with.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("call_sid")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^CA[0-9a-fA-F]{32}$")]
    public string? CallSid { get; init; }

    /// <summary>
    /// The Conference SID that identifies the conference associated with the recording, if a conference recording.
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
    /// The date and time in GMT that the resource was last updated, specified in <see href="https://www.ietf.org/rfc/rfc2822.txt">RFC 2822</see> format.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("date_updated")]
    public string? DateUpdated { get; init; }

    /// <summary>
    /// The start time of the recording in GMT and in <see href="https://www.php.net/manual/en/class.datetime.php#datetime.constants.rfc2822">RFC 2822</see> format.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("start_time")]
    public string? StartTime { get; init; }

    /// <summary>
    /// The length of the recording in seconds.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("duration")]
    public string? Duration { get; init; }

    /// <summary>
    /// The unique string that that we created to identify the Recording resource.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("sid")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^RE[0-9a-fA-F]{32}$")]
    public string? Sid { get; init; }

    /// <summary>
    /// The one-time cost of creating the recording in the <c>price_unit</c> currency.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("price")]
    public double? Price { get; init; }

    /// <summary>
    /// The URI of the resource, relative to <c>https://api.twilio.com</c>.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("uri")]
    public string? Uri { get; init; }

    /// <summary>
    /// How to decrypt the recording if it was encrypted using <see href="https://www.twilio.com/docs/voice/tutorials/voice-recording-encryption">Call Recording Encryption</see> feature.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("encryption_details")]
    public object? EncryptionDetails { get; init; }

    /// <summary>
    /// The currency used in the <c>price</c> property. Example: <c>USD</c>.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("price_unit")]
    public string? PriceUnit { get; init; }

    /// <summary>
    /// The status of the recording. Can be: <c>processing</c>, <c>completed</c> and <c>absent</c>. For more detailed statuses on in-progress recordings, check out how to <see href="https://www.twilio.com/docs/voice/api/recording#update-a-recording-resource">Update a Recording Resource</see>.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("status")]
    public CallRecordingEnumStatus? Status { get; init; }

    /// <summary>
    /// The number of channels in the final recording file.  Can be: <c>1</c>, or <c>2</c>. Separating a two leg call into two separate channels of the recording file is supported in <see href="https://www.twilio.com/docs/voice/twiml/dial#attributes-record">Dial</see> and <see href="https://www.twilio.com/docs/voice/make-calls">Outbound Rest API</see> record options.
    /// </summary>
    [JsonPropertyName("channels")]
    public int? Channels { get; init; } = 0;

    /// <summary>
    /// How the recording was created. Can be: <c>DialVerb</c>, <c>Conference</c>, <c>OutboundAPI</c>, <c>Trunking</c>, <c>RecordVerb</c>, <c>StartCallRecordingAPI</c>, and <c>StartConferenceRecordingAPI</c>.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("source")]
    public CallRecordingEnumSource? Source { get; init; }

    /// <summary>
    /// The error code that describes why the recording is <c>absent</c>. The error code is described in our <see href="https://www.twilio.com/docs/api/errors">Error Dictionary</see>. This value is null if the recording <c>status</c> is not <c>absent</c>.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("error_code")]
    public int? ErrorCode { get; init; }

    /// <summary>
    /// The recorded track. Can be: <c>inbound</c>, <c>outbound</c>, or <c>both</c>.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("track")]
    public string? Track { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
