using System.Text.Json.Serialization;
using Twilio.Core.Enum;

namespace Twilio.Models.Enums;

/// <summary>
/// The HTTP method we use to call <c>voice_fallback_url</c>. Can be: <c>GET</c> or <c>POST</c>.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<VoiceFallbackMethod>))]
public sealed record VoiceFallbackMethod : StringEnum<VoiceFallbackMethod>
{
    private VoiceFallbackMethod(string value) : base(value)
    {
    }

    public static readonly VoiceFallbackMethod Get = new("GET");

    public static readonly VoiceFallbackMethod Post = new("POST");

    public static VoiceFallbackMethod FromValue(string value) => FromValueCore(value);
}
