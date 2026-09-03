using System.Text.Json.Serialization;
using Twilio.Core.Enum;

namespace Twilio.Models.Enums;

/// <summary>
/// The HTTP method we should use to call <c>conference_recording_status_callback</c>. Can be: <c>GET</c> or <c>POST</c> and defaults to <c>POST</c>.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<ConferenceRecordingStatusCallbackMethod>))]
public sealed record ConferenceRecordingStatusCallbackMethod : StringEnum<ConferenceRecordingStatusCallbackMethod>
{
    private ConferenceRecordingStatusCallbackMethod(string value) : base(value)
    {
    }

    public static readonly ConferenceRecordingStatusCallbackMethod Get = new("GET");

    public static readonly ConferenceRecordingStatusCallbackMethod Post = new("POST");

    public static ConferenceRecordingStatusCallbackMethod FromValue(string value) => FromValueCore(value);
}
