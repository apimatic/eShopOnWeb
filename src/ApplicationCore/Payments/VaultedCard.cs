namespace Microsoft.eShopWeb.ApplicationCore.Payments;

/// <summary>
/// A card that PayPal has vaulted. Carries only the opaque vault token id (used to charge the card
/// later) plus safe-to-display descriptors — never the PAN or CVV.
/// </summary>
public sealed record VaultedCard(
    string VaultTokenId,
    string? Brand,
    string? Last4,
    string? Expiry,
    string? CardHolderName);
