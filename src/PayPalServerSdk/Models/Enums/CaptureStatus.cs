using System;
using System.Text.Json.Serialization;
using PayPalServerSdk.Core.Enum;

namespace PayPalServerSdk.Models.Enums;

/// <summary>
/// The status of the captured payment.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<CaptureStatus>))]
public sealed record CaptureStatus : OpenStringEnum<CaptureStatus>
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

    public TResult Match<TResult>(Func<TResult> onCompleted,
        Func<TResult> onDeclined,
        Func<TResult> onPartiallyRefunded,
        Func<TResult> onPending,
        Func<TResult> onRefunded,
        Func<TResult> onFailed,
        Func<string, TResult> otherwise) =>
        this switch
        {
            _ when this == Completed => onCompleted(),
            _ when this == Declined => onDeclined(),
            _ when this == PartiallyRefunded => onPartiallyRefunded(),
            _ when this == Pending => onPending(),
            _ when this == Refunded => onRefunded(),
            _ when this == Failed => onFailed(),
            _ => otherwise(Value)
        };

    public void Match(Action onCompleted,
        Action onDeclined,
        Action onPartiallyRefunded,
        Action onPending,
        Action onRefunded,
        Action onFailed,
        Action<string> otherwise)
    {
        if (this == Completed) onCompleted();
        else if (this == Declined) onDeclined();
        else if (this == PartiallyRefunded) onPartiallyRefunded();
        else if (this == Pending) onPending();
        else if (this == Refunded) onRefunded();
        else if (this == Failed) onFailed();
        else otherwise(Value);
    }
}
