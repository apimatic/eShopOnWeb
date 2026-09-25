using System;
using System.Text.Json.Serialization;
using PayPalServerSdk.Core.Enum;

namespace PayPalServerSdk.Models.Enums;

/// <summary>
/// This field indicates the status of PayPal's Checkout experience throughout the order lifecycle. The values reflect the current stage of the checkout process.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<ExperienceStatus>))]
public sealed record ExperienceStatus : OpenStringEnum<ExperienceStatus>
{
    private ExperienceStatus(string value) : base(value)
    {
    }

    /// <summary>
    /// PayPal checkout process has not yet begun.
    /// </summary>
    public static readonly ExperienceStatus NotStarted = new("NOT_STARTED");

    /// <summary>
    /// PayPal checkout initiated. User is on the checkout page for order review before approval.
    /// </summary>
    public static readonly ExperienceStatus InProgress = new("IN_PROGRESS");

    /// <summary>
    /// PayPal checkout is canceled (by closing the checkout window or clicking cancel) before the order approval.
    /// </summary>
    public static readonly ExperienceStatus Canceled = new("CANCELED");

    /// <summary>
    /// Order is approved. User has completed the checkout process.
    /// </summary>
    public static readonly ExperienceStatus Approved = new("APPROVED");

    public TResult Match<TResult>(Func<TResult> onNotStarted,
        Func<TResult> onInProgress,
        Func<TResult> onCanceled,
        Func<TResult> onApproved,
        Func<string, TResult> otherwise) =>
        this switch
        {
            _ when this == NotStarted => onNotStarted(),
            _ when this == InProgress => onInProgress(),
            _ when this == Canceled => onCanceled(),
            _ when this == Approved => onApproved(),
            _ => otherwise(Value)
        };

    public void Match(Action onNotStarted,
        Action onInProgress,
        Action onCanceled,
        Action onApproved,
        Action<string> otherwise)
    {
        if (this == NotStarted) onNotStarted();
        else if (this == InProgress) onInProgress();
        else if (this == Canceled) onCanceled();
        else if (this == Approved) onApproved();
        else otherwise(Value);
    }
}
