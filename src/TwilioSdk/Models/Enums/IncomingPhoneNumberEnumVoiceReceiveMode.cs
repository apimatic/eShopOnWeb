using System.Text.Json.Serialization;
using TwilioSdk.Core.Enum;

namespace TwilioSdk.Models.Enums;

[JsonConverter(typeof(StringEnumConverter<IncomingPhoneNumberEnumVoiceReceiveMode>))]
public sealed record IncomingPhoneNumberEnumVoiceReceiveMode : StringEnum<IncomingPhoneNumberEnumVoiceReceiveMode>
{
    private IncomingPhoneNumberEnumVoiceReceiveMode(string value) : base(value)
    {
    }

    public static readonly IncomingPhoneNumberEnumVoiceReceiveMode Voice = new("voice");

    public static readonly IncomingPhoneNumberEnumVoiceReceiveMode Fax = new("fax");

    public static IncomingPhoneNumberEnumVoiceReceiveMode FromValue(string value) => FromValueCore(value);
}
