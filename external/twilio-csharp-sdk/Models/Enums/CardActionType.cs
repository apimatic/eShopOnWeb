using System.Text.Json.Serialization;
using Twilio.Core.Enum;

namespace Twilio.Models.Enums;

[JsonConverter(typeof(StringEnumConverter<CardActionType>))]
public sealed record CardActionType : StringEnum<CardActionType>
{
    private CardActionType(string value) : base(value)
    {
    }

    public static readonly CardActionType Url = new("URL");

    public static readonly CardActionType PhoneNumber = new("PHONE_NUMBER");

    public static readonly CardActionType QuickReply = new("QUICK_REPLY");

    public static readonly CardActionType CopyCode = new("COPY_CODE");

    public static readonly CardActionType VoiceCall = new("VOICE_CALL");

    public static CardActionType FromValue(string value) => FromValueCore(value);
}
