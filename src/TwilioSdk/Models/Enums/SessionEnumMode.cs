using System.Text.Json.Serialization;
using TwilioSdk.Core.Enum;

namespace TwilioSdk.Models.Enums;

/// <summary>
/// The Mode of the Session. Can be: <c>message-only</c>, <c>voice-only</c>, or <c>voice-and-message</c>.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<SessionEnumMode>))]
public sealed record SessionEnumMode : StringEnum<SessionEnumMode>
{
    private SessionEnumMode(string value) : base(value)
    {
    }

    public static readonly SessionEnumMode MessageOnly = new("message-only");

    public static readonly SessionEnumMode VoiceOnly = new("voice-only");

    public static readonly SessionEnumMode VoiceAndMessage = new("voice-and-message");

    public static SessionEnumMode FromValue(string value) => FromValueCore(value);
}
