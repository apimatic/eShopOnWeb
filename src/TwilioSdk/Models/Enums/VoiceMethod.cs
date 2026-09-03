using System.Text.Json.Serialization;
using TwilioSdk.Core.Enum;

namespace TwilioSdk.Models.Enums;

/// <summary>
/// The HTTP method we use to call <c>voice_url</c>. Can be: <c>GET</c> or <c>POST</c>.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<VoiceMethod>))]
public sealed record VoiceMethod : StringEnum<VoiceMethod>
{
    private VoiceMethod(string value) : base(value)
    {
    }

    public static readonly VoiceMethod Get = new("GET");

    public static readonly VoiceMethod Post = new("POST");

    public static VoiceMethod FromValue(string value) => FromValueCore(value);
}
