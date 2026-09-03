using System.Text.Json.Serialization;
using Twilio.Core.Enum;

namespace Twilio.Models.Enums;

/// <summary>
/// The method used to verify ownership of the number to be hosted. Can be: <c>phone-call</c> or <c>phone-bill</c> and the default is <c>phone-call</c>.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<DependentOrderEnumVerificationType>))]
public sealed record DependentOrderEnumVerificationType : StringEnum<DependentOrderEnumVerificationType>
{
    private DependentOrderEnumVerificationType(string value) : base(value)
    {
    }

    public static readonly DependentOrderEnumVerificationType PhoneCall = new("phone-call");

    public static readonly DependentOrderEnumVerificationType PhoneBill = new("phone-bill");

    public static DependentOrderEnumVerificationType FromValue(string value) => FromValueCore(value);
}
