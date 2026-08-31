using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Abstraction over the payment processor (PayPal). All shapes below map onto the
/// PayPal OpenAPI specifications in api-specs/paypal; the implementation lives in
/// Infrastructure. Full card details flow through this interface only - they are
/// never persisted or logged.
/// </summary>
public interface IPaymentGateway
{
    /// <summary>
    /// Creates a PayPal order with intent=AUTHORIZE and authorizes it, placing a hold
    /// on the funds without taking them. Provide either full card details or the
    /// vault token id of a saved card.
    /// </summary>
    Task<GatewayAuthorization> AuthorizeCardPaymentAsync(
        string referenceId,
        string invoiceId,
        GatewayMoney amount,
        GatewayCard? card,
        string? vaultTokenId,
        string idempotencyKey,
        CancellationToken cancellationToken = default);

    Task<GatewayAuthorizationStatus> GetAuthorizationAsync(string authorizationId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Renews a stale authorization. PayPal allows reauthorizing once, from day 4 to
    /// day 29 after the original authorization.
    /// </summary>
    Task<GatewayAuthorization> ReauthorizeAsync(string authorizationId, GatewayMoney amount, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Captures (takes) the held funds for an authorization.
    /// </summary>
    Task<GatewayCapture> CaptureAuthorizationAsync(string authorizationId, GatewayMoney amount, string invoiceId, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Voids an authorization, releasing the shopper's held funds.
    /// </summary>
    Task VoidAuthorizationAsync(string authorizationId, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Refunds a captured payment, in part (amount supplied) or in full (amount null).
    /// </summary>
    Task<GatewayRefund> RefundCaptureAsync(string captureId, GatewayMoney? amount, string idempotencyKey, string? noteToPayer, CancellationToken cancellationToken = default);

    /// <summary>
    /// Vaults a card for the given customer and returns the vault token plus safe
    /// display data (brand, last digits, expiry).
    /// </summary>
    Task<GatewayVaultedCard> VaultCardAsync(string customerId, GatewayCard card, string idempotencyKey, CancellationToken cancellationToken = default);

    Task DeleteVaultedCardAsync(string vaultTokenId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns PayPal's own record of transactions for the range, covering every page.
    /// </summary>
    Task<IReadOnlyList<GatewayTransaction>> SearchTransactionsAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}

public sealed record GatewayMoney(string CurrencyCode, string Value);

public sealed record GatewayAddress(
    string? AddressLine1,
    string? AddressLine2,
    string? City,
    string? State,
    string? PostalCode,
    string CountryCode);

public sealed record GatewayCard(
    string Number,
    string Expiry,
    string SecurityCode,
    string? CardholderName,
    GatewayAddress? BillingAddress);

public sealed record GatewayAuthorization(
    string? PayPalOrderId,
    string AuthorizationId,
    string Status,
    GatewayMoney? Amount,
    DateTimeOffset? ExpiresAt);

public sealed record GatewayAuthorizationStatus(
    string AuthorizationId,
    string Status,
    GatewayMoney? Amount,
    DateTimeOffset? ExpiresAt);

public sealed record GatewayCapture(
    string CaptureId,
    string Status,
    GatewayMoney Amount,
    GatewayMoney? GrossAmount,
    GatewayMoney? PayPalFee,
    GatewayMoney? NetAmount);

public sealed record GatewayRefund(
    string RefundId,
    string Status,
    GatewayMoney? Amount);

public sealed record GatewayVaultedCard(
    string VaultTokenId,
    string? Brand,
    string? LastDigits,
    string? Expiry,
    string? CardholderName);

public sealed record GatewayTransaction(
    string TransactionId,
    string? ReferenceId,
    string? EventCode,
    string? Status,
    GatewayMoney? Amount,
    GatewayMoney? FeeAmount,
    string? InvoiceId,
    string? CustomField,
    DateTimeOffset? InitiationDate,
    DateTimeOffset? UpdatedDate);
