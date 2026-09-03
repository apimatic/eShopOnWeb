using System.Text.Json.Serialization;
using Twilio.Core.Enum;

namespace Twilio.Models.Enums;

/// <summary>
/// Whether the phone number requires an <see href="https://www.twilio.com/docs/usage/api/address">Address</see> registered with Twilio. Can be: <c>none</c>, <c>any</c>, <c>local</c>, or <c>foreign</c>.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<IncomingPhoneNumberTollFreeEnumAddressRequirement>))]
public sealed record IncomingPhoneNumberTollFreeEnumAddressRequirement : StringEnum<IncomingPhoneNumberTollFreeEnumAddressRequirement>
{
    private IncomingPhoneNumberTollFreeEnumAddressRequirement(string value) : base(value)
    {
    }

    public static readonly IncomingPhoneNumberTollFreeEnumAddressRequirement None = new("none");

    public static readonly IncomingPhoneNumberTollFreeEnumAddressRequirement Any = new("any");

    public static readonly IncomingPhoneNumberTollFreeEnumAddressRequirement Local = new("local");

    public static readonly IncomingPhoneNumberTollFreeEnumAddressRequirement Foreign = new("foreign");

    public static IncomingPhoneNumberTollFreeEnumAddressRequirement FromValue(string value) =>
        FromValueCore(value);
}
