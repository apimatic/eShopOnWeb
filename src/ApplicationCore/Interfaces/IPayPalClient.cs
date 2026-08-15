using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// A thin, typed client over the PayPal REST APIs described by the OpenAPI specs in
/// <c>api-specs/</c>. It owns OAuth token acquisition and the exact request/response shapes; the
/// application services orchestrate the business flow on top of it. Every method maps to a single
/// documented PayPal operation.
/// </summary>
public interface IPayPalClient
{
    // ---- Checkout Orders v2 ----

    /// <summary>Creates a PayPal order with intent=AUTHORIZE for the given amount, tagging the
    /// purchase unit with <paramref name="invoiceId"/> so it can be reconciled later.</summary>
    Task<PayPalOrderResult> CreateAuthorizeOrderAsync(decimal amount, string currency, string invoiceId,
        string requestId, CancellationToken ct = default);

    /// <summary>Authorizes (holds) the funds on a PayPal order using raw card details.</summary>
    Task<PayPalAuthorizationResult> AuthorizeOrderWithCardAsync(string payPalOrderId, CardDetails card,
        string requestId, CancellationToken ct = default);

    /// <summary>Authorizes (holds) the funds on a PayPal order using a saved (vaulted) card.</summary>
    Task<PayPalAuthorizationResult> AuthorizeOrderWithVaultAsync(string payPalOrderId, string vaultTokenId,
        string requestId, CancellationToken ct = default);

    // ---- Payments v2 (authorizations / captures / refunds) ----

    Task<PayPalAuthorizationResult> GetAuthorizationAsync(string authorizationId, CancellationToken ct = default);

    /// <summary>Renews a stale authorization, returning a fresh authorization to capture against.</summary>
    Task<PayPalAuthorizationResult> ReauthorizeAsync(string authorizationId, decimal amount, string currency,
        string requestId, CancellationToken ct = default);

    /// <summary>Captures (takes) the held funds. Returns PayPal's captured amount, fee and net proceeds.</summary>
    Task<PayPalCaptureResult> CaptureAuthorizationAsync(string authorizationId, decimal amount, string currency,
        string requestId, CancellationToken ct = default);

    /// <summary>Voids (releases) a held authorization so no money moves.</summary>
    Task VoidAuthorizationAsync(string authorizationId, string requestId, CancellationToken ct = default);

    /// <summary>Refunds a capture, fully (amount null) or partially. The idempotency key makes a repeat
    /// under the same key a no-op on PayPal's side.</summary>
    Task<PayPalRefundResult> RefundCaptureAsync(string captureId, decimal? amount, string currency,
        string idempotencyKey, CancellationToken ct = default);

    // ---- Vault Payment Tokens v3 ----

    /// <summary>Vaults a card, optionally associating it with an existing PayPal customer id.</summary>
    Task<VaultCardResult> VaultCardAsync(CardDetails card, string? customerId, string requestId,
        CancellationToken ct = default);

    Task<IReadOnlyList<VaultedCard>> ListVaultCardsAsync(string customerId, CancellationToken ct = default);

    Task DeleteVaultCardAsync(string vaultTokenId, CancellationToken ct = default);

    // ---- Transaction Search v1 (reconciliation) ----

    /// <summary>Returns PayPal's own record of transactions across the whole date range, paging through
    /// every page rather than returning only the first.</summary>
    Task<IReadOnlyList<PayPalTransaction>> SearchTransactionsAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken ct = default);
}

/// <summary>Raw card details supplied for a one-off payment or to vault. Never persisted or logged.</summary>
public record CardDetails(
    string Number,
    string Expiry,
    string SecurityCode,
    string? Name,
    CardBillingAddress? BillingAddress);

public record CardBillingAddress(
    string? AddressLine1,
    string? AddressLine2,
    string? AdminArea2,
    string? AdminArea1,
    string? PostalCode,
    string CountryCode);

public record PayPalOrderResult(string Id, string Status);

public record PayPalAuthorizationResult(
    string OrderStatus,
    string? AuthorizationId,
    string? Status,
    decimal? Amount,
    string? Currency,
    DateTimeOffset? ExpiresAt,
    string? CardBrand,
    string? CardLast4,
    string? VaultTokenId,
    string? CustomerId);

public record PayPalCaptureResult(
    string CaptureId,
    string Status,
    decimal GrossAmount,
    decimal? PayPalFee,
    decimal? NetAmount,
    string Currency);

public record PayPalRefundResult(string RefundId, string Status, decimal Amount, string Currency);

public record VaultCardResult(string VaultTokenId, string? CustomerId, string? Brand, string? Last4, string? Expiry);

public record VaultedCard(string VaultTokenId, string? Brand, string? Last4, string? Expiry);

public record PayPalTransaction(
    string? TransactionId,
    string? EventCode,
    string? Status,
    decimal? Amount,
    string? Currency,
    decimal? Fee,
    string? InvoiceId,
    DateTimeOffset? InitiationDate);
