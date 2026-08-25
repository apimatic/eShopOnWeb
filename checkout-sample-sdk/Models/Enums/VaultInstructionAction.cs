using System.Text.Json.Serialization;
using PayPalServerSdk.Core.Enum;

namespace PayPalServerSdk.Models.Enums;

/// <summary>
/// Vault Instruction on action to be performed after a successful payer approval.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<VaultInstructionAction>))]
public sealed record VaultInstructionAction : StringEnum<VaultInstructionAction>
{
    private VaultInstructionAction(string value) : base(value)
    {
    }

    /// <summary>
    /// Vault the payment method after API caller performs a successful POST on Payment Tokens.
    /// </summary>
    public static readonly VaultInstructionAction OnCreatePaymentTokens = new("ON_CREATE_PAYMENT_TOKENS");

    /// <summary>
    /// Vault the payment method on successful payer authentication and approval.
    /// </summary>
    public static readonly VaultInstructionAction OnPayerApproval = new("ON_PAYER_APPROVAL");

    public static VaultInstructionAction FromValue(string value) => FromValueCore(value);
}
