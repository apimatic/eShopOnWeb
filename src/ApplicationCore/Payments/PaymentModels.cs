namespace Microsoft.eShopWeb.ApplicationCore.Payments;

/// <summary>
/// Raw card details supplied by the shopper for a one-off payment or to be vaulted. This type is a
/// transient carrier only — it is never persisted or logged.
/// </summary>
public record CardDetails(
    string Number,
    string Expiry,          // "YYYY-MM"
    string SecurityCode,
    string CardholderName,
    CardBillingAddress BillingAddress);

/// <summary>Billing address for a card, using PayPal's address field names.</summary>
public record CardBillingAddress(
    string AddressLine1,
    string? AddressLine2,
    string AdminArea2,      // city / town
    string? AdminArea1,     // state / province (optional — some countries have none)
    string PostalCode,
    string CountryCode);    // 2-letter ISO country code

/// <summary>Outcome of charging a card (one-off or vaulted).</summary>
public record CardChargeResult(
    string PayPalOrderId,
    string CaptureId,
    string Status,
    string? Last4,
    string? CardBrand);

/// <summary>Outcome of vaulting (saving) a card.</summary>
public record VaultedCardResult(
    string VaultId,
    string CardBrand,
    string Last4,
    string Expiry);

/// <summary>Outcome of a refund.</summary>
public record RefundResult(
    string RefundId,
    string Status);
