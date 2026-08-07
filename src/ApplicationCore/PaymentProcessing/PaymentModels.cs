namespace Microsoft.eShopWeb.ApplicationCore.PaymentProcessing;

/// <summary>
/// Raw card details for a one-off payment or for vaulting. These are passed straight through to
/// the payment provider and are never persisted in the application's own database, nor logged.
/// </summary>
public record CardDetails(
    string CardholderName,
    string Number,
    int ExpiryMonth,
    int ExpiryYear,
    string SecurityCode,
    BillingAddress BillingAddress);

/// <summary>Billing address for a card.</summary>
public record BillingAddress(
    string AddressLine1,
    string City,
    string PostalCode,
    string CountryCode,
    string? State = null,
    string? AddressLine2 = null);

/// <summary>A one-off card charge for an order.</summary>
public record CardPaymentRequest(
    string ReferenceId,
    decimal Amount,
    string Currency,
    CardDetails Card,
    string IdempotencyKey);

/// <summary>A charge against a previously saved (vaulted) card.</summary>
public record SavedCardPaymentRequest(
    string ReferenceId,
    decimal Amount,
    string Currency,
    string VaultTokenId,
    string IdempotencyKey);

/// <summary>A request to save (vault) a card for later reuse.</summary>
public record VaultCardRequest(
    CardDetails Card,
    string IdempotencyKey);

/// <summary>A full refund of a captured payment.</summary>
public record RefundPaymentRequest(
    string CaptureId,
    string IdempotencyKey);

/// <summary>Outcome of a successful capture: the provider order id, capture id and capture status.</summary>
public record PaymentResult(string PayPalOrderId, string CaptureId, string Status);

/// <summary>A vaulted card: the token used to charge it later plus a PCI-safe descriptor.</summary>
public record VaultedCard(string TokenId, string? CardBrand, string? Last4, string? Expiry);

/// <summary>Outcome of a successful refund.</summary>
public record RefundResult(string RefundId, string Status);
