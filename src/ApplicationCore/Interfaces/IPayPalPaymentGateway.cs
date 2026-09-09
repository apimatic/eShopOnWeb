using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Application-facing abstraction over PayPal. All PayPal SDK types stay behind this seam; the rest
/// of the app deals only in the plain DTOs below. The implementation lives in the PublicApi host
/// (the only project that references the PayPal SDK).
/// </summary>
public interface IPayPalPaymentGateway
{
    /// <summary>
    /// Authorizes (places a hold on) the order total. Supply either one-off card details or a vault id
    /// (a saved card), never both. The held amount equals the requested amount to the cent.
    /// </summary>
    Task<AuthorizationResult> AuthorizeAsync(AuthorizeCommand command, CancellationToken ct);

    /// <summary>
    /// Captures (takes) a previously authorized payment. If the authorization has gone stale it is
    /// renewed (reauthorized) first; the effective authorization id is returned. Throws
    /// <see cref="Exceptions.PaymentReauthorizationException"/> when a stale authorization cannot be renewed.
    /// </summary>
    Task<CaptureResult> CaptureAsync(CaptureCommand command, CancellationToken ct);

    /// <summary>Releases a held authorization before fulfilment, so no money moves.</summary>
    Task VoidAsync(string authorizationId, string idempotencyKey, CancellationToken ct);

    /// <summary>Refunds a captured payment, in full (no amount) or in part.</summary>
    Task<RefundResult> RefundAsync(RefundCommand command, CancellationToken ct);

    /// <summary>Vaults a card for reuse, returning the vault id, customer id and a safe descriptor.</summary>
    Task<VaultedCardResult> VaultCardAsync(VaultCardCommand command, CancellationToken ct);

    /// <summary>Deletes a vaulted card so it can no longer be used to pay.</summary>
    Task DeleteVaultedCardAsync(string vaultId, CancellationToken ct);

    /// <summary>
    /// Lists PayPal's own record of transactions across the whole date range (all pages), for
    /// reconciliation against eShop orders.
    /// </summary>
    Task<IReadOnlyList<PayPalTransaction>> SearchTransactionsAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct);
}

/// <summary>Card details for a one-off payment or a vault request. Never persisted or logged.</summary>
public record CardDetails(
    string Number,
    string Expiry,          // ISO-8601 YYYY-MM
    string SecurityCode,
    string? CardholderName,
    CardBillingAddress? BillingAddress);

public record CardBillingAddress(
    string? AddressLine1,
    string? City,
    string? State,
    string? PostalCode,
    string? CountryCode);   // ISO-3166-1 alpha-2

public record CardSummary(string? Brand, string? Last4, string? Expiry);

public record AuthorizeCommand(
    int OrderId,
    decimal Amount,
    string IdempotencyKey,
    CardDetails? Card,
    string? VaultId);

public record AuthorizationResult(
    string PayPalOrderId,
    string AuthorizationId,
    string Status,
    decimal AuthorizedAmount);

public record CaptureCommand(
    string AuthorizationId,
    decimal Amount,
    string IdempotencyKey);

public record CaptureResult(
    string CaptureId,
    string EffectiveAuthorizationId,
    string Status,
    decimal Gross,
    decimal? Fee,
    decimal? Net);

public record RefundCommand(
    string CaptureId,
    decimal? Amount,        // null = full refund
    string IdempotencyKey,
    string? NoteToPayer);

public record RefundResult(string RefundId, string Status, decimal Amount);

public record VaultCardCommand(
    string? ExistingCustomerId,
    CardDetails Card,
    string IdempotencyKey);

public record VaultedCardResult(string VaultId, string CustomerId, CardSummary Card);

/// <summary>PayPal's own record of one transaction, as returned by transaction search.</summary>
public record PayPalTransaction(
    string? TransactionId,
    string? Status,
    decimal? Amount,
    string? Currency,
    decimal? Fee,
    string? InvoiceId,
    string? CustomField,
    DateTimeOffset? InitiationDate);
