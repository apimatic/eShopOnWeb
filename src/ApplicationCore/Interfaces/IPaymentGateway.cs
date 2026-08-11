using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// The payment processor, as the rest of the application sees it. The concrete
/// implementation talks to PayPal; nothing above this interface knows that. Every
/// method throws <see cref="Exceptions.PaymentException"/> on failure, with a
/// caller-safe message and an HTTP-shaped status code.
/// </summary>
public interface IPaymentGateway
{
    /// <summary>
    /// Places a hold for <paramref name="amount"/> (to the cent) using either a raw
    /// <paramref name="card"/> for a one-off payment, or a previously vaulted card named
    /// by <paramref name="vaultId"/>. The money is not taken. <paramref name="idempotencyKey"/>
    /// makes a repeated call return the same hold rather than placing a second one.
    /// </summary>
    Task<GatewayAuthorization> AuthorizeAsync(string idempotencyKey, decimal amount, string currency,
        CardDetails? card, string? vaultId, CancellationToken cancellationToken = default);

    /// <summary>Takes the held money. Returns what PayPal reported: captured amount, fee and net.</summary>
    Task<GatewayCapture> CaptureAsync(string idempotencyKey, string authorizationId, decimal amount,
        string currency, bool finalCapture, CancellationToken cancellationToken = default);

    /// <summary>
    /// Renews a hold that has gone stale before fulfilment. Throws a
    /// <see cref="Exceptions.PaymentException"/> with an operator-actionable message when the
    /// authorization can no longer be renewed.
    /// </summary>
    Task<GatewayAuthorization> ReauthorizeAsync(string authorizationId, decimal amount, string currency,
        CancellationToken cancellationToken = default);

    /// <summary>Releases a hold before fulfilment, so no money moves.</summary>
    Task VoidAsync(string authorizationId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Refunds a capture, in full (<paramref name="amount"/> null) or in part. The
    /// <paramref name="idempotencyKey"/> ensures repeating a refund does not refund twice.
    /// </summary>
    Task<GatewayRefund> RefundAsync(string idempotencyKey, string captureId, decimal? amount,
        string currency, CancellationToken cancellationToken = default);

    /// <summary>Vaults a card for reuse and returns its token id plus a safe descriptor.</summary>
    Task<GatewayVaultedCard> VaultCardAsync(string idempotencyKey, string customerId, CardDetails card,
        CancellationToken cancellationToken = default);

    /// <summary>Removes a vaulted card so it can no longer be used to pay.</summary>
    Task DeleteVaultedCardAsync(string vaultId, CancellationToken cancellationToken = default);

    /// <summary>
    /// PayPal's own record of transactions across the whole <paramref name="from"/>..<paramref name="to"/>
    /// range (every page, not just the first), for reconciliation against eShop's records.
    /// </summary>
    Task<IReadOnlyList<GatewayTransaction>> SearchTransactionsAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken = default);
}

/// <summary>Raw card details for a one-off payment or to vault. Never stored, never logged.</summary>
public record CardDetails(
    string Number,
    string Expiry,
    string SecurityCode,
    string CardholderName,
    BillingAddress? BillingAddress);

public record BillingAddress(
    string AddressLine1,
    string? AddressLine2,
    string City,
    string? State,
    string PostalCode,
    string CountryCode);

public record GatewayAuthorization(string PayPalOrderId, string AuthorizationId, string Status);

public record GatewayCapture(
    string CaptureId,
    string Status,
    decimal GrossAmount,
    decimal? PayPalFee,
    decimal? NetAmount,
    string Currency);

public record GatewayRefund(string RefundId, string Status, decimal Amount, string Currency);

public record GatewayVaultedCard(string VaultId, string? Brand, string? Last4, string? Expiry);

public record GatewayTransaction(
    string TransactionId,
    string? Status,
    decimal? Amount,
    string? Currency,
    decimal? FeeAmount,
    DateTimeOffset? InitiationDate);
