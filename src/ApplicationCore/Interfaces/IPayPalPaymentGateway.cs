using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Card details for a one-off payment or a card being vaulted. These never touch the
/// application database or logs; they flow straight to PayPal.
/// </summary>
public record PaymentCard(
    string Number,
    string Expiry,          // YYYY-MM
    string? SecurityCode,
    string CardholderName,
    CardBillingAddress? BillingAddress);

public record CardBillingAddress(
    string? Line1,
    string? Line2,
    string? City,
    string? State,
    string? PostalCode,
    string? CountryCode);

/// <summary>An authorization request: pay with a raw card OR a previously vaulted card.</summary>
public record AuthorizeRequest(
    decimal Amount,
    string InvoiceId,
    string IdempotencyKey,
    PaymentCard? Card,
    string? VaultId);

public record AuthorizeResult(
    string PayPalOrderId,
    string AuthorizationId,
    string Status,
    DateTimeOffset? ExpiresAt);

public record AuthorizationDetails(string Status, DateTimeOffset? ExpiresAt);

public record CaptureResult(
    string CaptureId,
    string Status,
    decimal Gross,
    decimal Fee,
    decimal Net);

public record ReauthorizeResult(string AuthorizationId, string Status, DateTimeOffset? ExpiresAt);

public record RefundResult(string PayPalRefundId, string Status, decimal Amount);

public record VaultCardRequest(PaymentCard Card, string? CustomerId);

public record VaultCardResult(
    string TokenId,
    string CustomerId,
    string Brand,
    string LastFourDigits,
    string Expiry,
    string CardholderName);

/// <summary>A transaction as PayPal reports it, for reconciliation.</summary>
public record PayPalTransaction(
    string TransactionId,
    string Status,
    decimal Amount,
    string CurrencyCode,
    decimal? Fee,
    string? InvoiceId,
    DateTimeOffset? Date);

/// <summary>
/// Everything the integration needs PayPal to do. Implemented in Infrastructure over the
/// PayPal REST API (Orders v2, Payments v2, Vault v3, Transaction Search v1).
/// </summary>
public interface IPayPalPaymentGateway
{
    /// <summary>Place a hold on the money (Orders v2 create with intent=AUTHORIZE). Does not capture.</summary>
    Task<AuthorizeResult> AuthorizeAsync(AuthorizeRequest request, CancellationToken cancellationToken = default);

    /// <summary>Current status/expiry of an authorization.</summary>
    Task<AuthorizationDetails> GetAuthorizationAsync(string authorizationId, CancellationToken cancellationToken = default);

    /// <summary>Take the money that was held (Payments v2 capture). Returns PayPal's fee/net breakdown.</summary>
    Task<CaptureResult> CaptureAsync(string authorizationId, decimal amount, string invoiceId,
        string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>Renew a stale authorization so fulfilment can proceed.</summary>
    Task<ReauthorizeResult> ReauthorizeAsync(string authorizationId, decimal amount,
        string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>Release a hold before capture (Payments v2 void). No money moves.</summary>
    Task VoidAuthorizationAsync(string authorizationId, CancellationToken cancellationToken = default);

    /// <summary>Refund a captured payment, in full or in part. Idempotent per <paramref name="idempotencyKey"/>.</summary>
    Task<RefundResult> RefundAsync(string captureId, decimal amount, string invoiceId,
        string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>Save a card in the vault without a purchase (Vault v3 setup-token then payment-token).</summary>
    Task<VaultCardResult> VaultCardAsync(VaultCardRequest request, CancellationToken cancellationToken = default);

    /// <summary>Remove a vaulted card so it can no longer be used to pay.</summary>
    Task DeleteVaultedCardAsync(string tokenId, CancellationToken cancellationToken = default);

    /// <summary>
    /// PayPal's own record of transactions across the whole date range (chunked to respect the
    /// 31-day window limit and fully paginated).
    /// </summary>
    Task<IReadOnlyList<PayPalTransaction>> ListTransactionsAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken = default);
}
