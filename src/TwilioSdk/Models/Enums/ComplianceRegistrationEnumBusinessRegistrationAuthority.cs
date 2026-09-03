using System.Text.Json.Serialization;
using TwilioSdk.Core.Enum;

namespace TwilioSdk.Models.Enums;

/// <summary>
/// The authority that registered the business
/// </summary>
[JsonConverter(typeof(StringEnumConverter<ComplianceRegistrationEnumBusinessRegistrationAuthority>))]
public sealed record ComplianceRegistrationEnumBusinessRegistrationAuthority : StringEnum<ComplianceRegistrationEnumBusinessRegistrationAuthority>
{
    private ComplianceRegistrationEnumBusinessRegistrationAuthority(string value) : base(value)
    {
    }

    public static readonly ComplianceRegistrationEnumBusinessRegistrationAuthority UkCrn = new("UK:CRN");

    public static readonly ComplianceRegistrationEnumBusinessRegistrationAuthority UsEin = new("US:EIN");

    public static readonly ComplianceRegistrationEnumBusinessRegistrationAuthority CaCbn = new("CA:CBN");

    public static readonly ComplianceRegistrationEnumBusinessRegistrationAuthority AuAcn = new("AU:ACN");

    public static readonly ComplianceRegistrationEnumBusinessRegistrationAuthority Other = new("Other");

    public static ComplianceRegistrationEnumBusinessRegistrationAuthority FromValue(string value) =>
        FromValueCore(value);
}
