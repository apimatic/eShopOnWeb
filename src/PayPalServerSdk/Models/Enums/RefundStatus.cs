using System.Text.Json.Serialization;
using PayPalServerSdk.Core.Enum;

namespace PayPalServerSdk.Models.Enums;

/// <summary>
/// The status of the refund.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<RefundStatus>))]
public sealed record RefundStatus : StringEnum<RefundStatus>
{
    private RefundStatus(string value) : base(value)
    {
    }

    /// <summary>
    /// The refund was cancelled.
    /// </summary>
    public static readonly RefundStatus Cancelled = new("CANCELLED");

    /// <summary>
    /// The refund could not be processed.
    /// </summary>
    public static readonly RefundStatus Failed = new("FAILED");

    /// <summary>
    /// The refund is pending. For more information, see status_details.reason.
    /// </summary>
    public static readonly RefundStatus Pending = new("PENDING");

    /// <summary>
    /// The funds for this transaction were debited to the customer's account.
    /// </summary>
    public static readonly RefundStatus Completed = new("COMPLETED");

    public static RefundStatus FromValue(string value) => FromValueCore(value);
}
