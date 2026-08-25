namespace Microsoft.eShopWeb.ApplicationCore.PayPal;

public record PayPalVaultedCard(
    string VaultId,
    string? Brand,
    string? Last4,
    string? Expiry,
    string? CardType);
