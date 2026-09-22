using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// The boundary between eShop and PayPal. Every PayPal interaction goes through here; no PayPal SDK type
/// crosses this interface, so callers (endpoints, services) stay free of the SDK. Implementations translate
/// all SDK/transport failures into <see cref="Exceptions.PaymentGatewayException"/>.
/// </summary>
public interface IPayPalPaymentGateway
{
    /// <summary>
    /// Create a PayPal order for the amount and place a hold on the funds (authorize) — money is not taken.
    /// The amount held equals the order total to the cent. Funds come from a one-off card or a saved vault id.
    /// </summary>
    Task<PayPalAuthorizationResult> AuthorizeAsync(PayPalAuthorizeCommand command, CancellationToken ct);

    /// <summary>Read the current state of an authorization (status + expiry) — used to detect a stale hold.</summary>
    Task<PayPalAuthorizationState> GetAuthorizationAsync(string authorizationId, CancellationToken ct);

    /// <summary>Renew a stale authorization; returns the new authorization id and expiry.</summary>
    Task<PayPalAuthorizationState> ReauthorizeAsync(string authorizationId, string idempotencyKey, CancellationToken ct);

    /// <summary>Capture (take) the held funds. Returns the captured amount, PayPal's fee and the net proceeds.</summary>
    Task<PayPalCaptureResult> CaptureAsync(string authorizationId, string idempotencyKey, CancellationToken ct);

    /// <summary>Void (release) a held authorization before fulfilment — no money moves.</summary>
    Task VoidAsync(string authorizationId, string idempotencyKey, CancellationToken ct);

    /// <summary>Refund a capture in full (null amount) or in part. The idempotency key dedupes repeats.</summary>
    Task<PayPalRefundResult> RefundAsync(string captureId, decimal? amount, string currencyCode, string idempotencyKey, CancellationToken ct);

    /// <summary>Re-read a PayPal order after an ambiguous write, by PayPal order id.</summary>
    Task<PayPalOrderState?> GetOrderAsync(string payPalOrderId, CancellationToken ct);

    /// <summary>Vault (save) a card for reuse. Returns the token id and a safe descriptor (brand + last four).</summary>
    Task<PayPalVaultResult> VaultCardAsync(PayPalVaultCardCommand command, CancellationToken ct);

    /// <summary>Delete a vaulted card so it can no longer be used to pay.</summary>
    Task DeleteVaultedCardAsync(string vaultId, CancellationToken ct);

    /// <summary>
    /// List PayPal's own transactions across the whole [from,to] range (chunked to PayPal's 31-day limit and
    /// walked page by page), for reconciliation against eShop orders.
    /// </summary>
    Task<PayPalReconciliationResult> SearchTransactionsAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct);
}

// --- commands ---

/// <summary>Card details for a one-off payment OR a saved-card reference; exactly one is used.</summary>
public record PayPalAuthorizeCommand
{
    public required decimal Amount { get; init; }
    public required string CurrencyCode { get; init; }
    public required string InvoiceReference { get; init; }
    public string? Description { get; init; }

    /// <summary>Stable idempotency key for the create+authorize pair (PayPal-Request-Id).</summary>
    public required string IdempotencyKey { get; init; }

    /// <summary>One-off card. Null when paying with a saved card.</summary>
    public PayPalCardDetails? Card { get; init; }

    /// <summary>Saved-card vault token id. Null when paying with a one-off card.</summary>
    public string? VaultId { get; init; }
}

public record PayPalCardDetails
{
    public required string Number { get; init; }
    /// <summary>Expiry in YYYY-MM.</summary>
    public required string Expiry { get; init; }
    public string? SecurityCode { get; init; }
    public string? CardholderName { get; init; }
    public PayPalBillingAddress? BillingAddress { get; init; }
}

public record PayPalBillingAddress
{
    public string? AddressLine1 { get; init; }
    public string? AdminArea2 { get; init; } // city
    public string? AdminArea1 { get; init; } // state
    public string? PostalCode { get; init; }
    public string? CountryCode { get; init; }
}

public record PayPalVaultCardCommand
{
    public required string BuyerId { get; init; }
    public required PayPalCardDetails Card { get; init; }
    /// <summary>Existing PayPal customer id to attach the card to, if the shopper already has one.</summary>
    public string? PayPalCustomerId { get; init; }
    /// <summary>Our own stable customer reference (merchant_customer_id) for grouping the shopper's cards.</summary>
    public string? MerchantCustomerId { get; init; }
}

// --- results ---

public record PayPalAuthorizationResult
{
    public required string PayPalOrderId { get; init; }
    public required string AuthorizationId { get; init; }
    public string? Status { get; init; }
    public DateTimeOffset? AuthorizedAt { get; init; }
    public DateTimeOffset? ExpiresAt { get; init; }
}

public record PayPalAuthorizationState
{
    public required string AuthorizationId { get; init; }
    public string? Status { get; init; }
    public DateTimeOffset? ExpiresAt { get; init; }
}

public record PayPalCaptureResult
{
    public required string CaptureId { get; init; }
    public string? Status { get; init; }
    public DateTimeOffset? CapturedAt { get; init; }
    public decimal? GrossAmount { get; init; }
    public decimal? PayPalFee { get; init; }
    public decimal? NetAmount { get; init; }
}

public record PayPalRefundResult
{
    public required string RefundId { get; init; }
    public string? Status { get; init; }
    public decimal? Amount { get; init; }
}

public record PayPalOrderState
{
    public string? Status { get; init; }
    public string? AuthorizationId { get; init; }
    public string? AuthorizationStatus { get; init; }
}

public record PayPalVaultResult
{
    public required string VaultId { get; init; }
    public string? PayPalCustomerId { get; init; }
    public string? CardBrand { get; init; }
    public string? LastFourDigits { get; init; }
    public string? Expiry { get; init; }
}

/// <summary>A single PayPal transaction as reported by TransactionSearch.</summary>
public record PayPalTransaction
{
    public string? TransactionId { get; init; }
    public string? Status { get; init; }
    public decimal? Amount { get; init; }
    public string? CurrencyCode { get; init; }
    public string? InvoiceId { get; init; }
    public string? CustomField { get; init; }
    public DateTimeOffset? InitiationDate { get; init; }
}

/// <summary>The full reconciliation sweep result — every page of every ≤31-day window.</summary>
public record PayPalReconciliationResult
{
    public required IReadOnlyList<PayPalTransaction> Transactions { get; init; }
    public required int WindowsScanned { get; init; }
    public required int PagesScanned { get; init; }
    /// <summary>True when the whole range was walked without hitting any protective cap.</summary>
    public required bool Complete { get; init; }
}
