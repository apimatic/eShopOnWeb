using System.Text.Json.Serialization;
using Twilio.Core.Enum;

namespace Twilio.Models.Enums;

/// <summary>
/// How the recording was created. Can be: <c>DialVerb</c>, <c>Conference</c>, <c>OutboundAPI</c>, <c>Trunking</c>, <c>RecordVerb</c>, <c>StartCallRecordingAPI</c>, and <c>StartConferenceRecordingAPI</c>.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<CallRecordingEnumSource>))]
public sealed record CallRecordingEnumSource : StringEnum<CallRecordingEnumSource>
{
    private CallRecordingEnumSource(string value) : base(value)
    {
    }

    public static readonly CallRecordingEnumSource DialVerb = new("DialVerb");

    public static readonly CallRecordingEnumSource Conference = new("Conference");

    public static readonly CallRecordingEnumSource OutboundApi = new("OutboundAPI");

    public static readonly CallRecordingEnumSource Trunking = new("Trunking");

    public static readonly CallRecordingEnumSource RecordVerb = new("RecordVerb");

    public static readonly CallRecordingEnumSource StartCallRecordingApi = new("StartCallRecordingAPI");

    public static readonly CallRecordingEnumSource StartConferenceRecordingApi = new("StartConferenceRecordingAPI");

    public static CallRecordingEnumSource FromValue(string value) => FromValueCore(value);
}
