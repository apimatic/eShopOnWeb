using System.Text.Json.Serialization;
using Twilio.Core.Enum;

namespace Twilio.Models.Enums;

/// <summary>
/// Whether the phone number requires an <see href="https://www.twilio.com/docs/usage/api/address">Address</see> registered with Twilio. Can be: <c>none</c>, <c>any</c>, <c>local</c>, or <c>foreign</c>.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<IncomingPhoneNumberMobileEnumAddressRequirement>))]
public sealed record IncomingPhoneNumberMobileEnumAddressRequirement : StringEnum<IncomingPhoneNumberMobileEnumAddressRequirement>
{
    private IncomingPhoneNumberMobileEnumAddressRequirement(string value) : base(value)
    {
    }

    public static readonly IncomingPhoneNumberMobileEnumAddressRequirement None = new("none");

    public static readonly IncomingPhoneNumberMobileEnumAddressRequirement Any = new("any");

    public static readonly IncomingPhoneNumberMobileEnumAddressRequirement Local = new("local");

    public static readonly IncomingPhoneNumberMobileEnumAddressRequirement Foreign = new("foreign");

    public static IncomingPhoneNumberMobileEnumAddressRequirement FromValue(string value) =>
        FromValueCore(value);
}
