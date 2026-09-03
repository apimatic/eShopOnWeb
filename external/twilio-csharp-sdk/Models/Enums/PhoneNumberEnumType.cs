using System.Text.Json.Serialization;
using Twilio.Core.Enum;

namespace Twilio.Models.Enums;

[JsonConverter(typeof(StringEnumConverter<PhoneNumberEnumType>))]
public sealed record PhoneNumberEnumType : StringEnum<PhoneNumberEnumType>
{
    private PhoneNumberEnumType(string value) : base(value)
    {
    }

    public static readonly PhoneNumberEnumType Landline = new("landline");

    public static readonly PhoneNumberEnumType Mobile = new("mobile");

    public static readonly PhoneNumberEnumType Voip = new("voip");

    public static PhoneNumberEnumType FromValue(string value) => FromValueCore(value);
}
