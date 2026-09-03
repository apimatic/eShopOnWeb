using System.Text.Json.Serialization;
using Twilio.Core.Enum;

namespace Twilio.Models.Enums;

/// <summary>
/// The HTTP method we should use to call <c>voice_fallback_url</c>. Can be: <c>GET</c> or <c>POST</c>.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<VoiceFallbackMethod7>))]
public sealed record VoiceFallbackMethod7 : StringEnum<VoiceFallbackMethod7>
{
    private VoiceFallbackMethod7(string value) : base(value)
    {
    }

    public static readonly VoiceFallbackMethod7 Get = new("GET");

    public static readonly VoiceFallbackMethod7 Post = new("POST");

    public static VoiceFallbackMethod7 FromValue(string value) => FromValueCore(value);
}
