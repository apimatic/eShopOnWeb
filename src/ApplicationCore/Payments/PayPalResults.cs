namespace Microsoft.eShopWeb.ApplicationCore.Payments;

/// <summary>Outcome of capturing a PayPal payment for an order.</summary>
public record PayPalChargeResult(string PayPalOrderId, string CaptureId, string CaptureStatus);

/// <summary>Outcome of refunding a PayPal capture.</summary>
public record PayPalRefundResult(string RefundId, string RefundStatus);

/// <summary>
/// The safe, non-sensitive description of a card that PayPal returns after vaulting it.
/// Contains the vault token to charge the card later - never the full PAN.
/// </summary>
public record VaultedCard(
    string VaultToken,
    string CustomerId,
    string? Last4,
    string? Brand,
    string? Expiry,
    string? CardType);
