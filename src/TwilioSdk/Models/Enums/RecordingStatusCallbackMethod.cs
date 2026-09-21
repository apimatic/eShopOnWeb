using System.Text.Json.Serialization;
using TwilioSdk.Core.Enum;

namespace TwilioSdk.Models.Enums;

/// <summary>
/// The HTTP method we should use when calling the <c>recording_status_callback</c> URL. Can be: <c>GET</c> or <c>POST</c> and the default is <c>POST</c>.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<RecordingStatusCallbackMethod>))]
public sealed record RecordingStatusCallbackMethod : StringEnum<RecordingStatusCallbackMethod>
{
    private RecordingStatusCallbackMethod(string value) : base(value)
    {
    }

    public static readonly RecordingStatusCallbackMethod Get = new("GET");

    public static readonly RecordingStatusCallbackMethod Post = new("POST");

    public static RecordingStatusCallbackMethod FromValue(string value) => FromValueCore(value);
}
