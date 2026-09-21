using System.Text.Json.Serialization;
using TwilioSdk.Core.Enum;

namespace TwilioSdk.Models.Enums;

[JsonConverter(typeof(StringEnumConverter<IncomingPhoneNumberTollFreeEnumVoiceReceiveMode>))]
public sealed record IncomingPhoneNumberTollFreeEnumVoiceReceiveMode : StringEnum<IncomingPhoneNumberTollFreeEnumVoiceReceiveMode>
{
    private IncomingPhoneNumberTollFreeEnumVoiceReceiveMode(string value) : base(value)
    {
    }

    public static readonly IncomingPhoneNumberTollFreeEnumVoiceReceiveMode Voice = new("voice");

    public static readonly IncomingPhoneNumberTollFreeEnumVoiceReceiveMode Fax = new("fax");

    public static IncomingPhoneNumberTollFreeEnumVoiceReceiveMode FromValue(string value) =>
        FromValueCore(value);
}
