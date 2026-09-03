using System.Text.Json.Serialization;
using Twilio.Core.Enum;

namespace Twilio.Models.Enums;

/// <summary>
/// The HTTP method we should use to call <c>voice_url</c>. Can be: <c>GET</c> or <c>POST</c>.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<VoiceMethod7>))]
public sealed record VoiceMethod7 : StringEnum<VoiceMethod7>
{
    private VoiceMethod7(string value) : base(value)
    {
    }

    public static readonly VoiceMethod7 Get = new("GET");

    public static readonly VoiceMethod7 Post = new("POST");

    public static VoiceMethod7 FromValue(string value) => FromValueCore(value);
}
