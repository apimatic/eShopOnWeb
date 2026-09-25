using System;
using System.Text.Json.Serialization;
using PayPalServerSdk.Core.Enum;

namespace PayPalServerSdk.Models.Enums;

/// <summary>
/// The funds that are held on behalf of the merchant.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<DisbursementMode>))]
public sealed record DisbursementMode : OpenStringEnum<DisbursementMode>
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

    public TResult Match<TResult>(Func<TResult> onInstant, Func<TResult> onDelayed, Func<string, TResult> otherwise) =>
        this switch
        {
            _ when this == Instant => onInstant(),
            _ when this == Delayed => onDelayed(),
            _ => otherwise(Value)
        };

    public void Match(Action onInstant, Action onDelayed, Action<string> otherwise)
    {
        if (this == Instant) onInstant();
        else if (this == Delayed) onDelayed();
        else otherwise(Value);
    }
}
