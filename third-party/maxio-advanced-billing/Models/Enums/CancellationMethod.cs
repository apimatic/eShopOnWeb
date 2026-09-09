using System.Text.Json.Serialization;
using MaxioAdvancedBilling.Core.Enum;

namespace MaxioAdvancedBilling.Models.Enums;

/// <summary>
/// The process used to cancel the subscription, if the subscription has been canceled. It is nil if the subscription's state is not canceled.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<CancellationMethod>))]
public sealed record CancellationMethod : StringEnum<CancellationMethod>
{
    private CancellationMethod(string value) : base(value)
    {
    }

    public static readonly CancellationMethod MerchantUi = new("merchant_ui");

    public static readonly CancellationMethod MerchantApi = new("merchant_api");

    public static readonly CancellationMethod Dunning = new("dunning");

    public static readonly CancellationMethod BillingPortal = new("billing_portal");

    public static readonly CancellationMethod Unknown = new("unknown");

    public static readonly CancellationMethod Imported = new("imported");

    public static CancellationMethod FromValue(string value) => FromValueCore(value);
}
