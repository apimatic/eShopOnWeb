namespace Microsoft.eShopWeb.ApplicationCore.Payments;

/// <summary>Result of creating and capturing a PayPal order.</summary>
public record PayPalPaymentResult(string PayPalOrderId, string CaptureId, string Status);

/// <summary>Result of refunding a captured PayPal payment.</summary>
public record PayPalRefundResult(string RefundId, string Status);

/// <summary>A card saved to PayPal's Vault, described with safe details only.</summary>
public record PayPalVaultedCard(
    string VaultId,
    string CustomerId,
    string Brand,
    string Last4,
    string Expiry,
    string CardholderName);
