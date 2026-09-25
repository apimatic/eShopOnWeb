using System;
using System.Text.Json.Serialization;
using PayPalServerSdk.Core.Enum;

namespace PayPalServerSdk.Models.Enums;

/// <summary>
/// Liability shift indicator. The outcome of the issuer's authentication.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<LiabilityShiftIndicator>))]
public sealed record LiabilityShiftIndicator : OpenStringEnum<LiabilityShiftIndicator>
{
    private LiabilityShiftIndicator(string value) : base(value)
    {
    }

    /// <summary>
    /// Liability is with the merchant.
    /// </summary>
    public static readonly LiabilityShiftIndicator No = new("NO");

    /// <summary>
    /// Liability may shift to the card issuer.
    /// </summary>
    public static readonly LiabilityShiftIndicator Possible = new("POSSIBLE");

    /// <summary>
    /// The authentication system is not available.
    /// </summary>
    public static readonly LiabilityShiftIndicator Unknown = new("UNKNOWN");

    public TResult Match<TResult>(Func<TResult> onNo,
        Func<TResult> onPossible,
        Func<TResult> onUnknown,
        Func<string, TResult> otherwise) =>
        this switch
        {
            _ when this == No => onNo(),
            _ when this == Possible => onPossible(),
            _ when this == Unknown => onUnknown(),
            _ => otherwise(Value)
        };

    public void Match(Action onNo, Action onPossible, Action onUnknown, Action<string> otherwise)
    {
        if (this == No) onNo();
        else if (this == Possible) onPossible();
        else if (this == Unknown) onUnknown();
        else otherwise(Value);
    }
}
