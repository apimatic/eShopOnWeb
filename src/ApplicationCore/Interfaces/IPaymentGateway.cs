using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Abstraction over the payment processor (PayPal). All amounts are in the configured
/// currency; card details flow through but are never persisted.
/// </summary>
public interface IPaymentGateway
{
    Task<GatewayAuthorization> AuthorizeWithCardAsync(string reference, decimal amount, string currency,
        GatewayCardDetails card, string idempotencyKey, CancellationToken ct = default);

    Task<GatewayAuthorization> AuthorizeWithSavedCardAsync(string reference, decimal amount, string currency,
        string vaultTokenId, string idempotencyKey, CancellationToken ct = default);

    Task<GatewayAuthorizationState> GetAuthorizationAsync(string authorizationId, CancellationToken ct = default);

    Task<GatewayAuthorizationState> ReauthorizeAsync(string authorizationId, decimal amount,
        string currency, string idempotencyKey, CancellationToken ct = default);

    Task<GatewayCapture> CaptureAuthorizationAsync(string authorizationId, decimal amount,
        string currency, string idempotencyKey, CancellationToken ct = default);

    Task VoidAuthorizationAsync(string authorizationId, string idempotencyKey, CancellationToken ct = default);

    Task<GatewayRefund> RefundCaptureAsync(string captureId, decimal? amount, string currency,
        string idempotencyKey, CancellationToken ct = default);

    Task<GatewaySavedCard> SaveCardAsync(string merchantCustomerId, GatewayCardDetails card,
        string idempotencyKey, CancellationToken ct = default);

    Task DeleteSavedCardAsync(string paymentTokenId, CancellationToken ct = default);

    Task<IReadOnlyList<GatewayTransaction>> SearchTransactionsAsync(DateTimeOffset from,
        DateTimeOffset to, CancellationToken ct = default);
}

public record GatewayBillingAddress(
    string? AddressLine1,
    string? AddressLine2,
    string? City,
    string? State,
    string? PostalCode,
    string CountryCode);

public record GatewayCardDetails(
    string Number,
    string Expiry,
    string? SecurityCode,
    string? Name,
    GatewayBillingAddress? BillingAddress);

public record GatewayAuthorization(
    string PayPalOrderId,
    string AuthorizationId,
    string Status,
    DateTimeOffset? ExpiresAt,
    decimal Amount,
    bool PayerActionRequired);

public record GatewayAuthorizationState(
    string AuthorizationId,
    string Status,
    DateTimeOffset? ExpiresAt,
    decimal Amount);

public record GatewayCapture(
    string CaptureId,
    string Status,
    decimal GrossAmount,
    decimal? Fee,
    decimal? NetAmount);

public record GatewayRefund(
    string RefundId,
    string Status,
    decimal Amount);

public record GatewaySavedCard(
    string PaymentTokenId,
    string? PayPalCustomerId,
    string? Brand,
    string? LastDigits,
    string? Expiry,
    string? Name);

public record GatewayTransaction(
    string TransactionId,
    string? ReferenceId,
    string? ReferenceType,
    decimal? Amount,
    decimal? Fee,
    string? Status,
    DateTimeOffset? InitiatedAt,
    string? InvoiceId,
    string? CustomId);
