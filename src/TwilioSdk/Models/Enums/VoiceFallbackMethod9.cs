using System.Text.Json.Serialization;
using TwilioSdk.Core.Enum;

namespace TwilioSdk.Models.Enums;

/// <summary>
/// The HTTP method that we should use to call <c>voice_fallback_url</c>. Can be: <c>GET</c> or <c>POST</c> and defaults to <c>POST</c>.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<VoiceFallbackMethod9>))]
public sealed record VoiceFallbackMethod9 : StringEnum<VoiceFallbackMethod9>
{
    private VoiceFallbackMethod9(string value) : base(value)
    {
    }

    public static readonly VoiceFallbackMethod9 Get = new("GET");

    public static readonly VoiceFallbackMethod9 Post = new("POST");

    public static VoiceFallbackMethod9 FromValue(string value) => FromValueCore(value);
}
