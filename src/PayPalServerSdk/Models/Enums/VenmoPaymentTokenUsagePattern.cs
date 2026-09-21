using System.Text.Json.Serialization;
using PayPalServerSdk.Core.Enum;

namespace PayPalServerSdk.Models.Enums;

/// <summary>
/// Expected business/pricing model for the billing agreement.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<VenmoPaymentTokenUsagePattern>))]
public sealed record VenmoPaymentTokenUsagePattern : StringEnum<VenmoPaymentTokenUsagePattern>
{
    private VenmoPaymentTokenUsagePattern(string value) : base(value)
    {
    }

    /// <summary>
    /// On-demand instant payments – non-recurring, pre-paid, variable amount, variable frequency.
    /// </summary>
    public static readonly VenmoPaymentTokenUsagePattern Immediate = new("IMMEDIATE");

    /// <summary>
    /// Pay after use, non-recurring post-paid, variable amount, irregular frequency.
    /// </summary>
    public static readonly VenmoPaymentTokenUsagePattern Deferred = new("DEFERRED");

    /// <summary>
    /// Pay upfront fixed or variable amount on a fixed date before the goods/service is delivered.
    /// </summary>
    public static readonly VenmoPaymentTokenUsagePattern RecurringPrepaid = new("RECURRING_PREPAID");

    /// <summary>
    /// Pay on a fixed date based on usage or consumption after the goods/service is delivered.
    /// </summary>
    public static readonly VenmoPaymentTokenUsagePattern RecurringPostpaid = new("RECURRING_POSTPAID");

    /// <summary>
    /// Charge payer when the set amount is reached or monthly billing cycle, whichever comes first, before the goods/service is delivered.
    /// </summary>
    public static readonly VenmoPaymentTokenUsagePattern ThresholdPrepaid = new("THRESHOLD_PREPAID");

    /// <summary>
    /// Charge payer when the set amount is reached or monthly billing cycle, whichever comes first, after the goods/service is delivered.
    /// </summary>
    public static readonly VenmoPaymentTokenUsagePattern ThresholdPostpaid = new("THRESHOLD_POSTPAID");

    public static VenmoPaymentTokenUsagePattern FromValue(string value) => FromValueCore(value);
}
