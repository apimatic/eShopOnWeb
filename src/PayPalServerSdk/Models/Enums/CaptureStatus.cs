using System.Text.Json.Serialization;
using PayPalServerSdk.Core.Enum;

namespace PayPalServerSdk.Models.Enums;

/// <summary>
/// The status of the captured payment.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<CaptureStatus>))]
public sealed record CaptureStatus : StringEnum<CaptureStatus>
{
    private CaptureStatus(string value) : base(value)
    {
    }

    /// <summary>
    /// The funds for this captured payment were credited to the payee's PayPal account.
    /// </summary>
    public static readonly CaptureStatus Completed = new("COMPLETED");

    /// <summary>
    /// The funds could not be captured.
    /// </summary>
    public static readonly CaptureStatus Declined = new("DECLINED");

    /// <summary>
    /// An amount less than this captured payment's amount was partially refunded to the payer.
    /// </summary>
    public static readonly CaptureStatus PartiallyRefunded = new("PARTIALLY_REFUNDED");

    /// <summary>
    /// The funds for this captured payment was not yet credited to the payee's PayPal account. For more information, see status.details.
    /// </summary>
    public static readonly CaptureStatus Pending = new("PENDING");

    /// <summary>
    /// An amount greater than or equal to this captured payment's amount was refunded to the payer.
    /// </summary>
    public static readonly CaptureStatus Refunded = new("REFUNDED");

    /// <summary>
    /// There was an error while capturing payment.
    /// </summary>
    public static readonly CaptureStatus Failed = new("FAILED");

    public static CaptureStatus FromValue(string value) => FromValueCore(value);
}
