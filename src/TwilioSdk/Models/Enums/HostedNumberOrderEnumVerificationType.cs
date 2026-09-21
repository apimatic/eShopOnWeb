using System.Text.Json.Serialization;
using TwilioSdk.Core.Enum;

namespace TwilioSdk.Models.Enums;

/// <summary>
/// The method used to verify ownership of the number to be hosted. Can be: <c>phone-call</c> or <c>phone-bill</c> and the default is <c>phone-call</c>.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<HostedNumberOrderEnumVerificationType>))]
public sealed record HostedNumberOrderEnumVerificationType : StringEnum<HostedNumberOrderEnumVerificationType>
{
    private HostedNumberOrderEnumVerificationType(string value) : base(value)
    {
    }

    public static readonly HostedNumberOrderEnumVerificationType PhoneCall = new("phone-call");

    public static readonly HostedNumberOrderEnumVerificationType PhoneBill = new("phone-bill");

    public static HostedNumberOrderEnumVerificationType FromValue(string value) => FromValueCore(value);
}
