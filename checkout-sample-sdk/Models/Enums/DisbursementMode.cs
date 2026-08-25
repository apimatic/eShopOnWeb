using System.Text.Json.Serialization;
using PayPalServerSdk.Core.Enum;

namespace PayPalServerSdk.Models.Enums;

/// <summary>
/// The funds that are held on behalf of the merchant.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<DisbursementMode>))]
public sealed record DisbursementMode : StringEnum<DisbursementMode>
{
    private DisbursementMode(string value) : base(value)
    {
    }

    /// <summary>
    /// The funds are released to the merchant immediately.
    /// </summary>
    public static readonly DisbursementMode Instant = new("INSTANT");

    /// <summary>
    /// The funds are held for a finite number of days. The actual duration depends on the region and type of integration. You can release the funds through a referenced payout. Otherwise, the funds disbursed automatically after the specified duration.
    /// </summary>
    public static readonly DisbursementMode Delayed = new("DELAYED");

    public static DisbursementMode FromValue(string value) => FromValueCore(value);
}
