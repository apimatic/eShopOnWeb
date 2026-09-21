using System.Text.Json.Serialization;
using PayPalServerSdk.Core.Enum;

namespace PayPalServerSdk.Models.Enums;

/// <summary>
/// The status of the item shipment.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<OrderTrackerStatus>))]
public sealed record OrderTrackerStatus : StringEnum<OrderTrackerStatus>
{
    private OrderTrackerStatus(string value) : base(value)
    {
    }

    /// <summary>
    /// The shipment was cancelled and the tracking number no longer applies.
    /// </summary>
    public static readonly OrderTrackerStatus Cancelled = new("CANCELLED");

    /// <summary>
    /// The merchant has assigned a tracking number to the items being shipped from the Order. This does not correspond to the carrier's actual status for the shipment. The latest status of the parcel must be retrieved from the carrier.
    /// </summary>
    public static readonly OrderTrackerStatus Shipped = new("SHIPPED");

    public static OrderTrackerStatus FromValue(string value) => FromValueCore(value);
}
