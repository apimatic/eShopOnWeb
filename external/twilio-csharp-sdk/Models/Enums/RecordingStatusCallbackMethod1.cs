using System.Text.Json.Serialization;
using Twilio.Core.Enum;

namespace Twilio.Models.Enums;

/// <summary>
/// The HTTP method we should use to call <c>recording_status_callback</c>. Can be: <c>GET</c> or <c>POST</c> and the default is <c>POST</c>.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<RecordingStatusCallbackMethod1>))]
public sealed record RecordingStatusCallbackMethod1 : StringEnum<RecordingStatusCallbackMethod1>
{
    private RecordingStatusCallbackMethod1(string value) : base(value)
    {
    }

    public static readonly RecordingStatusCallbackMethod1 Get = new("GET");

    public static readonly RecordingStatusCallbackMethod1 Post = new("POST");

    public static RecordingStatusCallbackMethod1 FromValue(string value) => FromValueCore(value);
}
