using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>Raw card details, transient only — never persisted, never logged.</summary>
public sealed record CardDetails(
    string Number,
    string Expiry,
    string SecurityCode,
    string? CardholderName,
    string? AddressLine1,
    string? City,
    string? State,
    string? PostalCode,
    string? CountryCode);

public sealed record GatewayAuthorization(
    string PayPalOrderId,
    string AuthorizationId,
    string Status,
    DateTimeOffset? ExpiresAt,
    string? ProcessorResponseCode);

public sealed record GatewayAuthorizationState(
    string AuthorizationId,
    string Status,
    DateTimeOffset? ExpiresAt);

public sealed record GatewayCapture(
    string CaptureId,
    string Status,
    decimal? GrossAmount,
    decimal? PayPalFee,
    decimal? NetAmount);

public sealed record GatewayRefund(
    string RefundId,
    string Status,
    decimal? Amount);

public sealed record GatewayVaultedCard(
    string VaultTokenId,
    string? PayPalCustomerId,
    string? Brand,
    string? LastDigits,
    string? Expiry,
    string? CardholderName);

public sealed record GatewayTransaction(
    string? TransactionId,
    string? ReferenceId,
    string? ReferenceIdType,
    string? EventCode,
    decimal? Amount,
    string? Currency,
    decimal? Fee,
    string? Status,
    string? InvoiceId,
    string? CustomField,
    DateTimeOffset? InitiationDate,
    DateTimeOffset? UpdatedDate);

/// <summary>
/// The application's boundary to the payment provider (PayPal). All SDK error
/// translation happens behind this interface: implementations throw
/// <see cref="Exceptions.PaymentGatewayException"/> instead of SDK exceptions.
/// </summary>
public interface IPaymentGateway
{
    Task<GatewayAuthorization> AuthorizeCardPaymentAsync(int orderId, decimal amount, string currency,
        CardDetails card, string idempotencyKey, CancellationToken ct = default);

    Task<GatewayAuthorization> AuthorizeVaultedCardPaymentAsync(int orderId, decimal amount, string currency,
        string vaultTokenId, string idempotencyKey, CancellationToken ct = default);

    Task<GatewayAuthorizationState> GetAuthorizationAsync(string authorizationId, CancellationToken ct = default);

    Task<GatewayAuthorizationState> ReauthorizeAsync(string authorizationId, decimal amount, string currency,
        string idempotencyKey, CancellationToken ct = default);

    Task<GatewayCapture> CaptureAuthorizationAsync(string authorizationId, decimal amount, string currency,
        string idempotencyKey, CancellationToken ct = default);

    Task<string> VoidAuthorizationAsync(string authorizationId, string idempotencyKey, CancellationToken ct = default);

    Task<GatewayRefund> RefundCaptureAsync(string captureId, decimal amount, string currency,
        string idempotencyKey, string? noteToPayer = null, CancellationToken ct = default);

    Task<GatewayVaultedCard> VaultCardAsync(string shopperKey, string? payPalCustomerId, CardDetails card,
        string idempotencyKey, CancellationToken ct = default);

    Task DeleteVaultedCardAsync(string vaultTokenId, CancellationToken ct = default);

    /// <summary>All of the provider's transactions in [from, to], every page.</summary>
    Task<IReadOnlyList<GatewayTransaction>> SearchTransactionsAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken ct = default);
}
