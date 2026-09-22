using System.Text.Json.Serialization;
using TwilioSdk.Core.Enum;

namespace TwilioSdk.Models.Enums;

/// <summary>
/// Whether the phone number requires an <see href="https://www.twilio.com/docs/usage/api/address">Address</see> registered with Twilio. Can be: <c>none</c>, <c>any</c>, <c>local</c>, or <c>foreign</c>.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<DependentPhoneNumberEnumAddressRequirement>))]
public sealed record DependentPhoneNumberEnumAddressRequirement : StringEnum<DependentPhoneNumberEnumAddressRequirement>
{
    private DependentPhoneNumberEnumAddressRequirement(string value) : base(value)
    {
    }

    public static readonly DependentPhoneNumberEnumAddressRequirement None = new("none");

    public static readonly DependentPhoneNumberEnumAddressRequirement Any = new("any");

    public static readonly DependentPhoneNumberEnumAddressRequirement Local = new("local");

    public static readonly DependentPhoneNumberEnumAddressRequirement Foreign = new("foreign");

    public static DependentPhoneNumberEnumAddressRequirement FromValue(string value) => FromValueCore(value);
}
