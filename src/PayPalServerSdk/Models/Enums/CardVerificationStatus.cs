using System;
using System.Text.Json.Serialization;
using PayPalServerSdk.Core.Enum;

namespace PayPalServerSdk.Models.Enums;

/// <summary>
/// Verification status of Card.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<CardVerificationStatus>))]
public sealed record CardVerificationStatus : OpenStringEnum<CardVerificationStatus>
{
    private CardVerificationStatus(string value) : base(value)
    {
    }

    /// <summary>
    /// Card has been verified
    /// </summary>
    public static readonly CardVerificationStatus Verified = new("VERIFIED");

    /// <summary>
    /// Card verification has failed
    /// </summary>
    public static readonly CardVerificationStatus Failed = new("FAILED");

    public TResult Match<TResult>(Func<TResult> onVerified, Func<TResult> onFailed, Func<string, TResult> otherwise) =>
        this switch
        {
            _ when this == Verified => onVerified(),
            _ when this == Failed => onFailed(),
            _ => otherwise(Value)
        };

    public void Match(Action onVerified, Action onFailed, Action<string> otherwise)
    {
        if (this == Verified) onVerified();
        else if (this == Failed) onFailed();
        else otherwise(Value);
    }
}
