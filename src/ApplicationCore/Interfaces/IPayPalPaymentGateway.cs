using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Abstraction over PayPal's REST APIs (Orders v2, Payments v2, Vault v3) as described by the
/// PayPal OpenAPI specifications under api-specs/paypal. Application services depend on this
/// contract; the concrete implementation lives in Infrastructure so card data and HTTP concerns
/// never leak into the domain.
/// </summary>
public interface IPayPalPaymentGateway
{
    /// <summary>
    /// Creates and captures a PayPal order paid with a one-off card. <paramref name="idempotencyKey"/>
    /// is sent as PayPal-Request-Id so retries do not double-charge.
    /// </summary>
    Task<CardChargeResult> ChargeWithCardAsync(decimal amount, string currencyCode, CardDetails card,
        string idempotencyKey, string? invoiceId = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates and captures a PayPal order paid with a previously vaulted card, referenced by its vault token id.
    /// </summary>
    Task<CardChargeResult> ChargeWithVaultedCardAsync(decimal amount, string currencyCode, string vaultId,
        string idempotencyKey, string? invoiceId = null, CancellationToken cancellationToken = default);

    /// <summary>Issues a full refund of a captured payment. Idempotent via <paramref name="idempotencyKey"/>.</summary>
    Task<RefundResult> RefundCaptureAsync(string captureId, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>Stores a card in the PayPal vault and returns its token plus a safe descriptor.</summary>
    Task<VaultedCard> VaultCardAsync(CardDetails card, CancellationToken cancellationToken = default);

    /// <summary>Deletes a vaulted card so it can no longer be used to pay.</summary>
    Task DeleteVaultedCardAsync(string vaultId, CancellationToken cancellationToken = default);
}

/// <summary>Raw card details for a one-off payment or to be vaulted. Never persisted or logged.</summary>
public record CardDetails(
    string Number,
    string ExpiryMonthYear, // RFC 3339 year-month, i.e. "YYYY-MM"
    string SecurityCode,
    string CardholderName,
    CardBillingAddress? BillingAddress = null);

/// <summary>Optional card billing address, mapped to PayPal's portable address fields.</summary>
public record CardBillingAddress(
    string? AddressLine1 = null,
    string? AddressLine2 = null,
    string? City = null,        // admin_area_2
    string? State = null,       // admin_area_1
    string? PostalCode = null,
    string? CountryCode = null); // 2-letter ISO code

/// <summary>Outcome of a successful card charge (order create + capture).</summary>
public record CardChargeResult(
    string PayPalOrderId,
    string CaptureId,
    string Status,
    string? CardLast4,
    string? CardBrand);

/// <summary>Outcome of a successful refund.</summary>
public record RefundResult(string RefundId, string Status);

/// <summary>A card stored in the PayPal vault, described safely (never full details).</summary>
public record VaultedCard(
    string VaultId,
    string Last4,
    string? Brand,
    string? ExpiryMonthYear,
    string? CardholderName);
