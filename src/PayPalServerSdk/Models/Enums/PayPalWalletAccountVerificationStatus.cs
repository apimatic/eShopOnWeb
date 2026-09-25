using System;
using System.Text.Json.Serialization;
using PayPalServerSdk.Core.Enum;

namespace PayPalServerSdk.Models.Enums;

/// <summary>
/// The account status indicates whether the buyer has verified the financial details associated with their PayPal account.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<PayPalWalletAccountVerificationStatus>))]
public sealed record PayPalWalletAccountVerificationStatus : OpenStringEnum<PayPalWalletAccountVerificationStatus>
{
    private PayPalWalletAccountVerificationStatus(string value) : base(value)
    {
    }

    /// <summary>
    /// The buyer has completed the verification of the financial details associated with this PayPal account. For example: confirming their bank account.
    /// </summary>
    public static readonly PayPalWalletAccountVerificationStatus Verified = new("VERIFIED");

    /// <summary>
    /// The buyer has not completed the verification of the financial details associated with this PayPal account. For example: confirming their bank account.
    /// </summary>
    public static readonly PayPalWalletAccountVerificationStatus Unverified = new("UNVERIFIED");

    public TResult Match<TResult>(Func<TResult> onVerified,
        Func<TResult> onUnverified,
        Func<string, TResult> otherwise) =>
        this switch
        {
            _ when this == Verified => onVerified(),
            _ when this == Unverified => onUnverified(),
            _ => otherwise(Value)
        };

    public void Match(Action onVerified, Action onUnverified, Action<string> otherwise)
    {
        if (this == Verified) onVerified();
        else if (this == Unverified) onUnverified();
        else otherwise(Value);
    }
}
