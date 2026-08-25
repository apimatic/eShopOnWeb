using System.Text.Json.Serialization;
using PayPalServerSdk.Core.Enum;

namespace PayPalServerSdk.Models.Enums;

/// <summary>
/// The account status indicates whether the buyer has verified the financial details associated with their PayPal account.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<PayPalWalletAccountVerificationStatus>))]
public sealed record PayPalWalletAccountVerificationStatus : StringEnum<PayPalWalletAccountVerificationStatus>
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

    public static PayPalWalletAccountVerificationStatus FromValue(string value) => FromValueCore(value);
}
