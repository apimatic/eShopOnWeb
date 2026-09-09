using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// A thin, purpose-built gateway over the PayPal REST APIs used by this integration
/// (Orders v2, Payments v2, Vault v3, Transaction Search v1). Implementations own OAuth token
/// handling, base-URL resolution and error translation; callers work in domain terms.
/// </summary>
public interface IPayPalClient
{
    /// <summary>
    /// Creates a PayPal order with <c>intent=AUTHORIZE</c> funded by a one-off card and places the
    /// hold. Returns the resulting authorization.
    /// </summary>
    Task<PayPalAuthorizationResult> AuthorizeWithCardAsync(
        PayPalMoney amount, string invoiceId, string customId, PayPalCard card, string requestId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a PayPal order with <c>intent=AUTHORIZE</c> funded by a previously vaulted card and
    /// places the hold. Returns the resulting authorization.
    /// </summary>
    Task<PayPalAuthorizationResult> AuthorizeWithVaultedCardAsync(
        PayPalMoney amount, string invoiceId, string customId, string vaultTokenId, string requestId, CancellationToken cancellationToken = default);

    /// <summary>Reads the current state of an authorization (used to detect a stale hold).</summary>
    Task<PayPalAuthorizationState> GetAuthorizationAsync(string authorizationId, CancellationToken cancellationToken = default);

    /// <summary>Renews (reauthorizes) an authorization whose honor period has lapsed.</summary>
    Task<PayPalAuthorizationResult> ReauthorizeAsync(
        string authorizationId, PayPalMoney amount, string requestId, CancellationToken cancellationToken = default);

    /// <summary>Captures (takes the money for) an authorization. Returns the fee/net breakdown PayPal reports.</summary>
    Task<PayPalCaptureResult> CaptureAsync(string authorizationId, string requestId, CancellationToken cancellationToken = default);

    /// <summary>Voids (releases) an authorization before capture.</summary>
    Task VoidAsync(string authorizationId, string requestId, CancellationToken cancellationToken = default);

    /// <summary>Refunds a capture, in full (null amount) or in part.</summary>
    Task<PayPalRefundResult> RefundAsync(
        string captureId, PayPalMoney? amount, string invoiceId, string requestId, CancellationToken cancellationToken = default);

    /// <summary>Vaults a card for the given customer and returns the durable payment token + safe descriptors.</summary>
    Task<PayPalVaultedCard> VaultCardAsync(PayPalCard card, string customerId, string requestId, CancellationToken cancellationToken = default);

    /// <summary>Deletes a vaulted payment token so the saved card can no longer be charged.</summary>
    Task DeleteVaultedCardAsync(string vaultTokenId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists PayPal's own record of transactions across the whole date range (chunking the range and
    /// paging as needed), for reconciliation against eShop orders.
    /// </summary>
    Task<IReadOnlyList<PayPalTransaction>> ListTransactionsAsync(
        DateTimeOffset from, DateTimeOffset to, string? currencyCode, CancellationToken cancellationToken = default);
}

/// <summary>An amount in a currency, as PayPal models money.</summary>
public record PayPalMoney(decimal Value, string CurrencyCode);

/// <summary>Raw card details for a one-off payment or to vault. Never persisted or logged by this app.</summary>
public record PayPalCard(
    string Number,
    string Expiry,
    string SecurityCode,
    string? Name,
    PayPalBillingAddress? BillingAddress);

/// <summary>A card billing address.</summary>
public record PayPalBillingAddress(
    string CountryCode,
    string? AddressLine1 = null,
    string? AdminArea2 = null,
    string? AdminArea1 = null,
    string? PostalCode = null);

/// <summary>The outcome of placing (or renewing) a hold.</summary>
public record PayPalAuthorizationResult(
    string PayPalOrderId,
    string AuthorizationId,
    string Status,
    DateTimeOffset? ExpiresAt,
    string? CardBrand,
    string? CardLast4);

/// <summary>The current state of an authorization.</summary>
public record PayPalAuthorizationState(string Status, DateTimeOffset? ExpiresAt);

/// <summary>The outcome of a capture, including PayPal's fee and the net proceeds to the merchant.</summary>
public record PayPalCaptureResult(
    string CaptureId,
    string Status,
    decimal GrossAmount,
    decimal PayPalFee,
    decimal NetAmount);

/// <summary>The outcome of a refund.</summary>
public record PayPalRefundResult(string RefundId, string Status, decimal Amount);

/// <summary>A vaulted card: the durable token plus safe descriptors for display.</summary>
public record PayPalVaultedCard(
    string VaultTokenId,
    string CustomerId,
    string Brand,
    string Last4,
    string? CardholderName,
    string? Expiry);

/// <summary>A single transaction from PayPal's transaction-search report.</summary>
public record PayPalTransaction(
    string TransactionId,
    string? InvoiceId,
    string EventCode,
    string Status,
    decimal Amount,
    string CurrencyCode,
    DateTimeOffset InitiationDate);
