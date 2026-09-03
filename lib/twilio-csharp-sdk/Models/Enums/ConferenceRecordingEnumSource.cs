using System.Text.Json.Serialization;
using Twilio.Core.Enum;

namespace Twilio.Models.Enums;

/// <summary>
/// How the recording was created. Can be: <c>DialVerb</c>, <c>Conference</c>, <c>OutboundAPI</c>, <c>Trunking</c>, <c>RecordVerb</c>, <c>StartCallRecordingAPI</c>, <c>StartConferenceRecordingAPI</c>.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<ConferenceRecordingEnumSource>))]
public sealed record ConferenceRecordingEnumSource : StringEnum<ConferenceRecordingEnumSource>
{
    private ConferenceRecordingEnumSource(string value) : base(value)
    {
    }

    public static readonly ConferenceRecordingEnumSource DialVerb = new("DialVerb");

    public static readonly ConferenceRecordingEnumSource Conference = new("Conference");

    public static readonly ConferenceRecordingEnumSource OutboundApi = new("OutboundAPI");

    public static readonly ConferenceRecordingEnumSource Trunking = new("Trunking");

    public static readonly ConferenceRecordingEnumSource RecordVerb = new("RecordVerb");

    public static readonly ConferenceRecordingEnumSource StartCallRecordingApi = new("StartCallRecordingAPI");

    public static readonly ConferenceRecordingEnumSource StartConferenceRecordingApi = new("StartConferenceRecordingAPI");

    public static ConferenceRecordingEnumSource FromValue(string value) => FromValueCore(value);
}
