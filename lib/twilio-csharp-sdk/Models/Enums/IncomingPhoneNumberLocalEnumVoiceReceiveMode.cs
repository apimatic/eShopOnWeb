using System.Text.Json.Serialization;
using Twilio.Core.Enum;

namespace Twilio.Models.Enums;

[JsonConverter(typeof(StringEnumConverter<IncomingPhoneNumberLocalEnumVoiceReceiveMode>))]
public sealed record IncomingPhoneNumberLocalEnumVoiceReceiveMode : StringEnum<IncomingPhoneNumberLocalEnumVoiceReceiveMode>
{
    private IncomingPhoneNumberLocalEnumVoiceReceiveMode(string value) : base(value)
    {
    }

    public static readonly IncomingPhoneNumberLocalEnumVoiceReceiveMode Voice = new("voice");

    public static readonly IncomingPhoneNumberLocalEnumVoiceReceiveMode Fax = new("fax");

    public static IncomingPhoneNumberLocalEnumVoiceReceiveMode FromValue(string value) => FromValueCore(value);
}
