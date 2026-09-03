using System.Text.Json.Serialization;
using Twilio.Core.Enum;

namespace Twilio.Models.Enums;

/// <summary>
/// The method used to verify ownership of the number to be hosted. Can be: <c>phone-call</c> or <c>phone-bill</c> and the default is <c>phone-call</c>.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<HostedNumberOrderEnumVerificationType1>))]
public sealed record HostedNumberOrderEnumVerificationType1 : StringEnum<HostedNumberOrderEnumVerificationType1>
{
    private HostedNumberOrderEnumVerificationType1(string value) : base(value)
    {
    }

    public static readonly HostedNumberOrderEnumVerificationType1 PhoneCall = new("phone-call");

    public static HostedNumberOrderEnumVerificationType1 FromValue(string value) => FromValueCore(value);
}
