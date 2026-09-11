using System.Text.Json.Serialization;
using PayPalServerSdk.Core.Enum;

namespace PayPalServerSdk.Models.Enums;

/// <summary>
/// The vault status.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<VaultStatus>))]
public sealed record VaultStatus : StringEnum<VaultStatus>
{
    private VaultStatus(string value) : base(value)
    {
    }

    /// <summary>
    /// The payment source has been saved in your customer's vault. This vault status reflects <c>/v3/vault</c> status.
    /// </summary>
    public static readonly VaultStatus Vaulted = new("VAULTED");

    /// <summary>
    /// DEPRECATED. The payment source has been saved in your customer's vault. This status applies to deprecated integration patterns and will not be returned for v3/vault integrations.
    /// </summary>
    public static readonly VaultStatus Created = new("CREATED");

    /// <summary>
    /// Customer has approved the action of saving the specified payment_source into their vault. Use v3/vault/payment-tokens with given setup_token to save the payment source in the vault
    /// </summary>
    public static readonly VaultStatus Approved = new("APPROVED");

    public static VaultStatus FromValue(string value) => FromValueCore(value);
}
