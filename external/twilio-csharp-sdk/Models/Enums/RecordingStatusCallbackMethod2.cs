using System.Text.Json.Serialization;
using Twilio.Core.Enum;

namespace Twilio.Models.Enums;

/// <summary>
/// The HTTP method we should use when we call <c>recording_status_callback</c>. Can be: <c>GET</c> or <c>POST</c> and defaults to <c>POST</c>.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<RecordingStatusCallbackMethod2>))]
public sealed record RecordingStatusCallbackMethod2 : StringEnum<RecordingStatusCallbackMethod2>
{
    private RecordingStatusCallbackMethod2(string value) : base(value)
    {
    }

    public static readonly RecordingStatusCallbackMethod2 Get = new("GET");

    public static readonly RecordingStatusCallbackMethod2 Post = new("POST");

    public static RecordingStatusCallbackMethod2 FromValue(string value) => FromValueCore(value);
}
