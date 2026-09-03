using System.Text.Json.Serialization;
using Twilio.Core.Enum;

namespace Twilio.Models.Enums;

/// <summary>
/// Whether the phone number requires an <see href="https://www.twilio.com/docs/usage/api/address">Address</see> registered with Twilio. Can be: <c>none</c>, <c>any</c>, <c>local</c>, or <c>foreign</c>.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<IncomingPhoneNumberEnumAddressRequirement>))]
public sealed record IncomingPhoneNumberEnumAddressRequirement : StringEnum<IncomingPhoneNumberEnumAddressRequirement>
{
    private IncomingPhoneNumberEnumAddressRequirement(string value) : base(value)
    {
    }

    public static readonly IncomingPhoneNumberEnumAddressRequirement None = new("none");

    public static readonly IncomingPhoneNumberEnumAddressRequirement Any = new("any");

    public static readonly IncomingPhoneNumberEnumAddressRequirement Local = new("local");

    public static readonly IncomingPhoneNumberEnumAddressRequirement Foreign = new("foreign");

    public static IncomingPhoneNumberEnumAddressRequirement FromValue(string value) => FromValueCore(value);
}
