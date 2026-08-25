namespace Microsoft.eShopWeb.ApplicationCore.PayPal;

// Exactly one of Card or VaultId is set: a one-off card, or a previously saved (vaulted) card.
public class PayPalPaymentSource
{
    private PayPalPaymentSource(PayPalCardDetails? card, string? vaultId)
    {
        Card = card;
        VaultId = vaultId;
    }

    public static PayPalPaymentSource FromCard(PayPalCardDetails card) => new(card, null);

    public static PayPalPaymentSource FromVaultId(string vaultId) => new(null, vaultId);

    public PayPalCardDetails? Card { get; }
    public string? VaultId { get; }
}
