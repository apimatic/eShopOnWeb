using System.Text.Json.Serialization;
using Twilio.Core.Enum;

namespace Twilio.Models.Enums;

/// <summary>
/// How the recording was created. Can be: <c>DialVerb</c>, <c>Conference</c>, <c>OutboundAPI</c>, <c>Trunking</c>, <c>RecordVerb</c>, <c>StartCallRecordingAPI</c>, and <c>StartConferenceRecordingAPI</c>.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<RecordingEnumSource>))]
public sealed record RecordingEnumSource : StringEnum<RecordingEnumSource>
{
    private RecordingEnumSource(string value) : base(value)
    {
    }

    public static readonly RecordingEnumSource DialVerb = new("DialVerb");

    public static readonly RecordingEnumSource Conference = new("Conference");

    public static readonly RecordingEnumSource OutboundApi = new("OutboundAPI");

    public static readonly RecordingEnumSource Trunking = new("Trunking");

    public static readonly RecordingEnumSource RecordVerb = new("RecordVerb");

    public static readonly RecordingEnumSource StartCallRecordingApi = new("StartCallRecordingAPI");

    public static readonly RecordingEnumSource StartConferenceRecordingApi = new("StartConferenceRecordingAPI");

    public static RecordingEnumSource FromValue(string value) => FromValueCore(value);
}
