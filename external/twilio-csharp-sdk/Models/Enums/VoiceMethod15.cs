using System.Text.Json.Serialization;
using Twilio.Core.Enum;

namespace Twilio.Models.Enums;

/// <summary>
/// The HTTP method we should use to call <c>voice_url</c>
/// </summary>
[JsonConverter(typeof(StringEnumConverter<VoiceMethod15>))]
public sealed record VoiceMethod15 : StringEnum<VoiceMethod15>
{
    private VoiceMethod15(string value) : base(value)
    {
    }

    public static readonly VoiceMethod15 Get = new("GET");

    public static readonly VoiceMethod15 Post = new("POST");

    public static VoiceMethod15 FromValue(string value) => FromValueCore(value);
}
