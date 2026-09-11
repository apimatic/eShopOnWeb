using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Abstraction over the payment provider (PayPal). Implemented in Infrastructure so the
/// application core stays independent of the HTTP/PayPal details. Every method maps to a single
/// provider capability so the flows stay separately invocable.
/// </summary>
public interface IPaymentGateway
{
    /// <summary>
    /// Creates a provider order for <paramref name="amount"/> with an AUTHORIZE intent and places a
    /// hold, paying either with one-off <paramref name="card"/> details or a saved card
    /// (<paramref name="vaultId"/>). The money is held, not taken.
    /// </summary>
    Task<GatewayAuthorizationResult> AuthorizeAsync(
        GatewayAmount amount,
        GatewayCardDetails? card,
        string? vaultId,
        string orderReference,
        string idempotencyKey,
        CancellationToken cancellationToken = default);

    /// <summary>Refreshes a hold that has gone stale, returning the new authorization.</summary>
    Task<GatewayAuthorizationResult> ReauthorizeAsync(
        string authorizationId,
        GatewayAmount amount,
        CancellationToken cancellationToken = default);

    /// <summary>Captures (takes) the held funds, fully. Returns what the provider reported.</summary>
    Task<GatewayCaptureResult> CaptureAsync(
        string authorizationId,
        GatewayAmount amount,
        string orderReference,
        string idempotencyKey,
        CancellationToken cancellationToken = default);

    /// <summary>Voids a hold, releasing the funds without taking them.</summary>
    Task VoidAsync(
        string authorizationId,
        string idempotencyKey,
        CancellationToken cancellationToken = default);

    /// <summary>Refunds a capture, fully (null amount) or partially.</summary>
    Task<GatewayRefundResult> RefundAsync(
        string captureId,
        GatewayAmount? amount,
        string orderReference,
        string idempotencyKey,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Vaults a card for later reuse and returns its token plus a safe descriptor. When
    /// <paramref name="customerId"/> is supplied the token is associated with that provider
    /// customer; otherwise the provider generates one and returns it.
    /// </summary>
    Task<GatewayVaultResult> VaultCardAsync(
        GatewayCardDetails card,
        string? customerId,
        string idempotencyKey,
        CancellationToken cancellationToken = default);

    /// <summary>Removes a vaulted card so it can no longer be charged.</summary>
    Task DeleteVaultedCardAsync(
        string vaultId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the provider's own record of transactions for the whole date range (following
    /// pagination and provider range limits), for reconciliation against eShop orders.
    /// </summary>
    Task<IReadOnlyList<GatewayTransaction>> SearchTransactionsAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default);
}

public record GatewayAmount(decimal Value, string CurrencyCode);

/// <summary>One-off card details used only to talk to the provider; never persisted or logged by this app.</summary>
public record GatewayCardDetails(
    string Number,
    string Expiry,
    string SecurityCode,
    string? Name,
    GatewayBillingAddress? BillingAddress);

public record GatewayBillingAddress(
    string? AddressLine1,
    string? AddressLine2,
    string? AdminArea2,
    string? AdminArea1,
    string? PostalCode,
    string CountryCode);

public record GatewayAuthorizationResult(
    string ProviderOrderId,
    string AuthorizationId,
    string Status,
    DateTimeOffset? ExpiresAt);

public record GatewayCaptureResult(
    string CaptureId,
    string Status,
    decimal GrossAmount,
    decimal? PayPalFee,
    decimal? NetAmount,
    string CurrencyCode);

public record GatewayRefundResult(
    string RefundId,
    string Status,
    decimal Amount,
    string CurrencyCode);

public record GatewayVaultResult(
    string VaultId,
    string? CustomerId,
    string CardBrand,
    string LastFourDigits,
    string? Expiry,
    string? CardholderName);

public record GatewayTransaction(
    string TransactionId,
    string? EventCode,
    string Status,
    decimal? Amount,
    string? CurrencyCode,
    string? InvoiceId,
    string? CustomField,
    DateTimeOffset? InitiationDate);
