using System.Text.Json.Serialization;
using TwilioSdk.Core.Enum;

namespace TwilioSdk.Models.Enums;

[JsonConverter(typeof(StringEnumConverter<CallToActionActionType>))]
public sealed record CallToActionActionType : StringEnum<CallToActionActionType>
{
    private CallToActionActionType(string value) : base(value)
    {
    }

    public static readonly CallToActionActionType Url = new("URL");

    public static readonly CallToActionActionType PhoneNumber = new("PHONE_NUMBER");

    public static readonly CallToActionActionType CopyCode = new("COPY_CODE");

    public static readonly CallToActionActionType VoiceCall = new("VOICE_CALL");

    public static readonly CallToActionActionType VoiceCallRequest = new("VOICE_CALL_REQUEST");

    public static CallToActionActionType FromValue(string value) => FromValueCore(value);
}
