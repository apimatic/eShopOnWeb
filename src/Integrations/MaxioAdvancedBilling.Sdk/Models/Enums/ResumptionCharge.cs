using System.Text.Json.Serialization;
using MaxioAdvancedBilling.Core.Enum;

namespace MaxioAdvancedBilling.Models.Enums;

/// <summary>
/// (For calendar billing subscriptions only) The way that the resumed subscription's charge should be handled
/// </summary>
[JsonConverter(typeof(StringEnumConverter<ResumptionCharge>))]
public sealed record ResumptionCharge : StringEnum<ResumptionCharge>
{
    private ResumptionCharge(string value) : base(value)
    {
    }

    public static readonly ResumptionCharge Prorated = new("prorated");

    public static readonly ResumptionCharge Immediate = new("immediate");

    public static readonly ResumptionCharge Delayed = new("delayed");

    public static ResumptionCharge FromValue(string value) => FromValueCore(value);
}
