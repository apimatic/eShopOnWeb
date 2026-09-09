using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Abstraction over the PayPal REST API used by this integration. The implementation
/// (in Infrastructure) is the single place that talks to PayPal; the application core
/// depends only on this interface.
/// </summary>
public interface IPayPalClient
{
    /// <summary>
    /// Create a PayPal order with intent AUTHORIZE using a direct card or a vaulted card,
    /// placing a hold on the money equal to the amount. Does not capture.
    /// </summary>
    Task<PayPalAuthorizationResult> AuthorizeAsync(PayPalAuthorizeRequest request, CancellationToken cancellationToken = default);

    /// <summary>Capture (settle) a previously authorized payment. This is when money is taken.</summary>
    Task<PayPalCaptureResult> CaptureAuthorizationAsync(string authorizationId, decimal amount, string currency, string requestId, CancellationToken cancellationToken = default);

    /// <summary>Renew a stale authorization so its hold is valid again before capture.</summary>
    Task<PayPalAuthorizationResult> ReauthorizeAsync(string authorizationId, decimal amount, string currency, string requestId, CancellationToken cancellationToken = default);

    /// <summary>Release a hold without charging (cancel before fulfilment).</summary>
    Task VoidAuthorizationAsync(string authorizationId, string requestId, CancellationToken cancellationToken = default);

    /// <summary>Refund a captured payment, fully (null amount) or partially.</summary>
    Task<PayPalRefundResult> RefundCaptureAsync(string captureId, decimal? amount, string currency, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>Vault (save) a card and return a durable token plus a safe description.</summary>
    Task<PayPalVaultCardResult> VaultCardAsync(PayPalCardDetails card, CancellationToken cancellationToken = default);

    /// <summary>Delete a vaulted card so it can no longer be charged.</summary>
    Task DeletePaymentTokenAsync(string vaultId, CancellationToken cancellationToken = default);

    /// <summary>
    /// List PayPal's own record of transactions across a date range. Covers the whole
    /// range (handles date-window chunking and pagination), not just the first page.
    /// </summary>
    Task<IReadOnlyList<PayPalTransaction>> SearchTransactionsAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}

/// <summary>Card details for a one-off payment or a vault request. Never persisted by this app.</summary>
public record PayPalCardDetails(
    string Number,
    string Expiry, // YYYY-MM
    string SecurityCode,
    string? Name,
    PayPalBillingAddress? BillingAddress);

public record PayPalBillingAddress(
    string? AddressLine1,
    string? AddressLine2,
    string? AdminArea2, // city
    string? AdminArea1, // state
    string? PostalCode,
    string CountryCode);

/// <summary>Request to authorize an order total with a one-off card or a vaulted card.</summary>
public record PayPalAuthorizeRequest
{
    public required decimal Amount { get; init; }
    public required string Currency { get; init; }

    /// <summary>External reference (the eShop order) used to reconcile with PayPal later.</summary>
    public required string InvoiceId { get; init; }

    /// <summary>Idempotency key sent as PayPal-Request-Id.</summary>
    public required string RequestId { get; init; }

    /// <summary>A one-off card. Mutually exclusive with <see cref="VaultId"/>.</summary>
    public PayPalCardDetails? Card { get; init; }

    /// <summary>A saved card's vault token. Mutually exclusive with <see cref="Card"/>.</summary>
    public string? VaultId { get; init; }

    /// <summary>Human-readable description for the purchase unit.</summary>
    public string? Description { get; init; }
}

public record PayPalAuthorizationResult
{
    public required string PayPalOrderId { get; init; }
    public required string PayPalOrderStatus { get; init; }
    public required string AuthorizationId { get; init; }
    public required string AuthorizationStatus { get; init; }
    public required decimal Amount { get; init; }
    public required string Currency { get; init; }
    public DateTimeOffset? ExpiresAt { get; init; }
}

public record PayPalCaptureResult
{
    public required string CaptureId { get; init; }
    public required string Status { get; init; }
    public required decimal GrossAmount { get; init; }
    public required decimal PayPalFee { get; init; }
    public required decimal NetAmount { get; init; }
    public required string Currency { get; init; }
}

public record PayPalRefundResult
{
    public required string RefundId { get; init; }
    public required string Status { get; init; }
    public required decimal Amount { get; init; }
    public required string Currency { get; init; }
}

public record PayPalVaultCardResult
{
    public required string VaultId { get; init; }
    public string? Last4 { get; init; }
    public string? Brand { get; init; }
    public string? Expiry { get; init; }
    public string? Name { get; init; }
}

/// <summary>A single transaction as reported by PayPal Transaction Search.</summary>
public record PayPalTransaction
{
    public required string TransactionId { get; init; }
    public string? InvoiceId { get; init; }
    public string? Status { get; init; }
    public string? EventCode { get; init; }
    public decimal Amount { get; init; }
    public string? Currency { get; init; }
    public DateTimeOffset? InitiationDate { get; init; }
}
