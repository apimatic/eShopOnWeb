namespace Microsoft.eShopWeb.ApplicationCore.Payments;

/// <summary>Outcome of a successful create + capture at the payment provider.</summary>
public sealed record PaymentCaptureResult(string ProviderOrderId, string CaptureId, string Status);

/// <summary>Outcome of a successful full refund at the payment provider.</summary>
public sealed record PaymentRefundResult(string RefundId, string Status);

/// <summary>
/// Outcome of vaulting a card: the vault id used to charge it later, plus a safe descriptor for the
/// shopper to recognise which card it is. Never carries full card details.
/// </summary>
public sealed record VaultedCardResult(string VaultId, string Brand, string LastFourDigits, string Expiry);
