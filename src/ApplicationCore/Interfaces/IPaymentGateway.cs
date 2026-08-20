using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public record CardPaymentSource(
    string Number,
    string Expiry,
    string SecurityCode,
    string Name,
    CardBillingAddress? BillingAddress);

public record CardBillingAddress(
    string AddressLine1,
    string? AddressLine2,
    string AdminArea2,
    string AdminArea1,
    string PostalCode,
    string CountryCode);

public record AuthorizationResult(
    string PayPalOrderId,
    string PayPalOrderStatus,
    string AuthorizationId,
    string AuthorizationStatus,
    decimal AuthorizedAmount,
    string Currency,
    DateTimeOffset? ExpirationTime);

public record CaptureResult(
    string CaptureId,
    string CaptureStatus,
    decimal CapturedAmount,
    decimal? PaypalFee,
    decimal? NetProceeds,
    string Currency);

public record RefundResult(
    string PayPalRefundId,
    string Status,
    decimal Amount,
    string Currency);

public record VaultedCardResult(
    string PaymentTokenId,
    string? CustomerId,
    string LastDigits,
    string Brand,
    string Expiry,
    string? CardholderName);

public record GatewayTransaction(
    string TransactionId,
    string? ReferenceId,
    string? ReferenceIdType,
    string? EventCode,
    string? Status,
    decimal? Amount,
    decimal? FeeAmount,
    string? Currency,
    string? InvoiceId,
    string? CustomField,
    string? InstrumentType,
    DateTimeOffset? InitiationDate);

public interface IPaymentGateway
{
    Task<AuthorizationResult> AuthorizeCardAsync(
        string invoiceId,
        string customId,
        decimal amount,
        string currency,
        CardPaymentSource card,
        string idempotencyKey,
        CancellationToken cancellationToken = default);

    Task<AuthorizationResult> AuthorizeVaultedCardAsync(
        string invoiceId,
        string customId,
        decimal amount,
        string currency,
        string vaultId,
        string idempotencyKey,
        CancellationToken cancellationToken = default);

    Task<CaptureResult> CaptureAuthorizationAsync(
        string authorizationId,
        decimal amount,
        string currency,
        string invoiceId,
        string idempotencyKey,
        CancellationToken cancellationToken = default);

    Task<AuthorizationResult> ReauthorizeAsync(
        string authorizationId,
        decimal amount,
        string currency,
        string idempotencyKey,
        CancellationToken cancellationToken = default);

    Task VoidAuthorizationAsync(
        string authorizationId,
        string idempotencyKey,
        CancellationToken cancellationToken = default);

    Task<RefundResult> RefundCaptureAsync(
        string captureId,
        decimal? amount,
        string currency,
        string idempotencyKey,
        CancellationToken cancellationToken = default);

    Task<(string Status, DateTimeOffset? ExpirationTime)> GetAuthorizationAsync(
        string authorizationId,
        CancellationToken cancellationToken = default);

    Task<VaultedCardResult> VaultCardAsync(
        CardPaymentSource card,
        string? merchantCustomerId,
        string? paypalCustomerId,
        string idempotencyKey,
        CancellationToken cancellationToken = default);

    Task DeleteVaultedCardAsync(string paymentTokenId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<GatewayTransaction>> ListTransactionsAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default);
}
