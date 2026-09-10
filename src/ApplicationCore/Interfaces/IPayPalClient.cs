using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Card details for a one-off payment or for vaulting. These values are passed straight through to
/// PayPal and are never persisted in the application's own database or written to logs.
/// </summary>
public record PayPalCardDetails(
    string Number,
    string Expiry, // ISO-8601 YYYY-MM
    string SecurityCode,
    string? Name,
    string? AddressLine1,
    string? AddressLine2,
    string? AdminArea2, // city
    string? AdminArea1, // state
    string? PostalCode,
    string? CountryCode);

public record PayPalOrderResult(string Id, string Status);

public record PayPalAuthorizationResult(
    string Id,
    string Status,
    decimal Amount,
    string Currency,
    DateTimeOffset? ExpiresAt,
    string? CardLast4,
    string? CardBrand);

public record PayPalCaptureResult(
    string Id,
    string Status,
    decimal GrossAmount,
    decimal? PayPalFee,
    decimal? NetAmount,
    string Currency);

public record PayPalRefundResult(string Id, string Status, decimal Amount, string Currency);

public record PayPalVaultCardResult(string VaultId, string CustomerId, string? Brand, string? Last4, string? Expiry, string? CardholderName);

public record PayPalTransaction(
    string TransactionId,
    string? InvoiceId,
    string? CustomField,
    string Status,
    string? EventCode,
    decimal Amount,
    string Currency,
    DateTimeOffset InitiationDate);

/// <summary>
/// A hand-written client for the PayPal REST APIs, built to the OpenAPI specs in <c>api-specs/</c>
/// (Orders v2, Payments v2, Vault v3, Transaction Search v1). The application depends on this
/// abstraction rather than on any third-party PayPal SDK.
/// </summary>
public interface IPayPalClient
{
    /// <summary>Creates a PayPal order with intent=AUTHORIZE for the given amount.</summary>
    Task<PayPalOrderResult> CreateAuthorizeOrderAsync(
        decimal amount, string currency, string invoiceId, string customId, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>Authorizes (holds funds on) a previously-created PayPal order using raw card details.</summary>
    Task<PayPalAuthorizationResult> AuthorizeOrderWithCardAsync(
        string payPalOrderId, PayPalCardDetails card, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>Authorizes a previously-created PayPal order using a vaulted (saved) card.</summary>
    Task<PayPalAuthorizationResult> AuthorizeOrderWithVaultAsync(
        string payPalOrderId, string vaultId, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>Reads the current state of an authorization.</summary>
    Task<PayPalAuthorizationResult> GetAuthorizationAsync(string authorizationId, CancellationToken cancellationToken = default);

    /// <summary>Captures (takes) the funds held by an authorization.</summary>
    Task<PayPalCaptureResult> CaptureAuthorizationAsync(
        string authorizationId, decimal amount, string currency, string invoiceId, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>Creates a fresh authorization for a stale one so fulfilment can proceed.</summary>
    Task<PayPalAuthorizationResult> ReauthorizeAsync(
        string authorizationId, decimal amount, string currency, CancellationToken cancellationToken = default);

    /// <summary>Voids (releases) an authorization so no money moves.</summary>
    Task VoidAuthorizationAsync(string authorizationId, CancellationToken cancellationToken = default);

    /// <summary>Refunds a captured payment, in full (amount = null) or in part.</summary>
    Task<PayPalRefundResult> RefundCaptureAsync(
        string captureId, decimal? amount, string currency, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>Vaults a card for later reuse and returns a safe description of it.</summary>
    Task<PayPalVaultCardResult> VaultCardAsync(
        PayPalCardDetails card, string customerId, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>Removes a vaulted card so it can no longer be used to pay.</summary>
    Task DeleteVaultedCardAsync(string vaultId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns every PayPal transaction across the whole date range (all pages, chunked to respect
    /// PayPal's per-request window), for reconciliation against eShop orders.
    /// </summary>
    Task<IReadOnlyList<PayPalTransaction>> SearchTransactionsAsync(
        DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}
