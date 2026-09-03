using System.Text.Json.Serialization;
using Twilio.Core.Enum;

namespace Twilio.Models.Enums;

/// <summary>
/// Whether the phone number requires an <see href="https://www.twilio.com/docs/usage/api/address">Address</see> registered with Twilio. Can be: <c>none</c>, <c>any</c>, <c>local</c>, or <c>foreign</c>.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<IncomingPhoneNumberLocalEnumAddressRequirement>))]
public sealed record IncomingPhoneNumberLocalEnumAddressRequirement : StringEnum<IncomingPhoneNumberLocalEnumAddressRequirement>
{
    private IncomingPhoneNumberLocalEnumAddressRequirement(string value) : base(value)
    {
    }

    public static readonly IncomingPhoneNumberLocalEnumAddressRequirement None = new("none");

    public static readonly IncomingPhoneNumberLocalEnumAddressRequirement Any = new("any");

    public static readonly IncomingPhoneNumberLocalEnumAddressRequirement Local = new("local");

    public static readonly IncomingPhoneNumberLocalEnumAddressRequirement Foreign = new("foreign");

    public static IncomingPhoneNumberLocalEnumAddressRequirement FromValue(string value) =>
        FromValueCore(value);
}
