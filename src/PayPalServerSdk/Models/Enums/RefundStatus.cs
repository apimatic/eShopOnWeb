using System;
using System.Text.Json.Serialization;
using PayPalServerSdk.Core.Enum;

namespace PayPalServerSdk.Models.Enums;

/// <summary>
/// The status of the refund.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<RefundStatus>))]
public sealed record RefundStatus : OpenStringEnum<RefundStatus>
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

    public TResult Match<TResult>(Func<TResult> onCancelled,
        Func<TResult> onFailed,
        Func<TResult> onPending,
        Func<TResult> onCompleted,
        Func<string, TResult> otherwise) =>
        this switch
        {
            _ when this == Cancelled => onCancelled(),
            _ when this == Failed => onFailed(),
            _ when this == Pending => onPending(),
            _ when this == Completed => onCompleted(),
            _ => otherwise(Value)
        };

    public void Match(Action onCancelled,
        Action onFailed,
        Action onPending,
        Action onCompleted,
        Action<string> otherwise)
    {
        if (this == Cancelled) onCancelled();
        else if (this == Failed) onFailed();
        else if (this == Pending) onPending();
        else if (this == Completed) onCompleted();
        else otherwise(Value);
    }
}
