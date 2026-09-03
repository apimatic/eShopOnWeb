using System.Text.Json.Serialization;
using Twilio.Core.Enum;

namespace Twilio.Models.Enums;

[JsonConverter(typeof(StringEnumConverter<IncomingPhoneNumberMobileEnumVoiceReceiveMode>))]
public sealed record IncomingPhoneNumberMobileEnumVoiceReceiveMode : StringEnum<IncomingPhoneNumberMobileEnumVoiceReceiveMode>
{
    private IncomingPhoneNumberMobileEnumVoiceReceiveMode(string value) : base(value)
    {
    }

    public static readonly IncomingPhoneNumberMobileEnumVoiceReceiveMode Voice = new("voice");

    public static readonly IncomingPhoneNumberMobileEnumVoiceReceiveMode Fax = new("fax");

    public static IncomingPhoneNumberMobileEnumVoiceReceiveMode FromValue(string value) => FromValueCore(value);
}
