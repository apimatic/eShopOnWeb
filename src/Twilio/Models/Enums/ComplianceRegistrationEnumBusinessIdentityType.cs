using System.Text.Json.Serialization;
using Twilio.Core.Enum;

namespace Twilio.Models.Enums;

/// <summary>
/// The type of business identity.  Can be <c>direct customer</c> or <c>ISV</c>.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<ComplianceRegistrationEnumBusinessIdentityType>))]
public sealed record ComplianceRegistrationEnumBusinessIdentityType : StringEnum<ComplianceRegistrationEnumBusinessIdentityType>
{
    private ComplianceRegistrationEnumBusinessIdentityType(string value) : base(value)
    {
    }

    public static readonly ComplianceRegistrationEnumBusinessIdentityType DirectCustomer = new("direct_customer");

    public static readonly ComplianceRegistrationEnumBusinessIdentityType IsvResellerOrPartner = new("isv_reseller_or_partner");

    public static readonly ComplianceRegistrationEnumBusinessIdentityType Unknown = new("unknown");

    public static ComplianceRegistrationEnumBusinessIdentityType FromValue(string value) =>
        FromValueCore(value);
}
