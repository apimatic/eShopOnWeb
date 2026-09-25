using System;
using System.Text.Json.Serialization;
using PayPalServerSdk.Core.Enum;

namespace PayPalServerSdk.Models.Enums;

/// <summary>
/// The vault status.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<PayPalWalletVaultStatus>))]
public sealed record PayPalWalletVaultStatus : OpenStringEnum<PayPalWalletVaultStatus>
{
    private PayPalWalletVaultStatus(string value) : base(value)
    {
    }

    /// <summary>
    /// The payment source has been saved in your customer's vault. This vault status reflects <c>/v3/vault</c> status.
    /// </summary>
    public static readonly PayPalWalletVaultStatus Vaulted = new("VAULTED");

    /// <summary>
    /// DEPRECATED. The payment source has been saved in your customer's vault. This status applies to deprecated integration patterns and will not be returned for v3/vault integrations.
    /// </summary>
    public static readonly PayPalWalletVaultStatus Created = new("CREATED");

    /// <summary>
    /// Customer has approved the action of saving the specified payment_source into their vault. Use v3/vault/payment-tokens with given setup_token to save the payment source in the vault
    /// </summary>
    public static readonly PayPalWalletVaultStatus Approved = new("APPROVED");

    public TResult Match<TResult>(Func<TResult> onVaulted,
        Func<TResult> onCreated,
        Func<TResult> onApproved,
        Func<string, TResult> otherwise) =>
        this switch
        {
            _ when this == Vaulted => onVaulted(),
            _ when this == Created => onCreated(),
            _ when this == Approved => onApproved(),
            _ => otherwise(Value)
        };

    public void Match(Action onVaulted, Action onCreated, Action onApproved, Action<string> otherwise)
    {
        if (this == Vaulted) onVaulted();
        else if (this == Created) onCreated();
        else if (this == Approved) onApproved();
        else otherwise(Value);
    }
}
