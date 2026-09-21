using System.Text.Json.Serialization;
using TwilioSdk.Core.Enum;

namespace TwilioSdk.Models.Enums;

/// <summary>
/// The HTTP method that we should use to call <c>voice_url</c>. Can be: <c>GET</c> or <c>POST</c> and defaults to <c>POST</c>.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<VoiceMethod9>))]
public sealed record VoiceMethod9 : StringEnum<VoiceMethod9>
{
    private VoiceMethod9(string value) : base(value)
    {
    }

    public static readonly VoiceMethod9 Get = new("GET");

    public static readonly VoiceMethod9 Post = new("POST");

    public static VoiceMethod9 FromValue(string value) => FromValueCore(value);
}
