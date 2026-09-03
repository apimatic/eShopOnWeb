using System.Text.Json.Serialization;
using Twilio.Core.Enum;

namespace Twilio.Models.Enums;

/// <summary>
/// The HTTP method we use to call <c>voice_status_callback_url</c>. Either <c>GET</c> or <c>POST</c>.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<VoiceStatusCallbackMethod>))]
public sealed record VoiceStatusCallbackMethod : StringEnum<VoiceStatusCallbackMethod>
{
    private VoiceStatusCallbackMethod(string value) : base(value)
    {
    }

    public static readonly VoiceStatusCallbackMethod Get = new("GET");

    public static readonly VoiceStatusCallbackMethod Post = new("POST");

    public static VoiceStatusCallbackMethod FromValue(string value) => FromValueCore(value);
}
