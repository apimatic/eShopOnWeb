using System.Text.Json.Serialization;
using TwilioSdk.Core.Enum;

namespace TwilioSdk.Models.Enums;

/// <summary>
/// The HTTP method we should use to call <c>voice_status_callback_url</c>. Can be: <c>GET</c> or <c>POST</c>.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<VoiceStatusCallbackMethod1>))]
public sealed record VoiceStatusCallbackMethod1 : StringEnum<VoiceStatusCallbackMethod1>
{
    private VoiceStatusCallbackMethod1(string value) : base(value)
    {
    }

    public static readonly VoiceStatusCallbackMethod1 Get = new("GET");

    public static readonly VoiceStatusCallbackMethod1 Post = new("POST");

    public static VoiceStatusCallbackMethod1 FromValue(string value) => FromValueCore(value);
}
