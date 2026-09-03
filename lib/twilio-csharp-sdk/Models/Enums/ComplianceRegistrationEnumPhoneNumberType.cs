using System.Text.Json.Serialization;
using Twilio.Core.Enum;

namespace Twilio.Models.Enums;

/// <summary>
/// The type of phone number of the Bundle's ownership request.  Can be <c>local</c>, <c>mobile</c>, <c>national</c>, or <c>toll-free</c>.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<ComplianceRegistrationEnumPhoneNumberType>))]
public sealed record ComplianceRegistrationEnumPhoneNumberType : StringEnum<ComplianceRegistrationEnumPhoneNumberType>
{
    private ComplianceRegistrationEnumPhoneNumberType(string value) : base(value)
    {
    }

    public static readonly ComplianceRegistrationEnumPhoneNumberType Local = new("local");

    public static readonly ComplianceRegistrationEnumPhoneNumberType National = new("national");

    public static readonly ComplianceRegistrationEnumPhoneNumberType Mobile = new("mobile");

    public static readonly ComplianceRegistrationEnumPhoneNumberType TollFree = new("toll-free");

    public static ComplianceRegistrationEnumPhoneNumberType FromValue(string value) => FromValueCore(value);
}
