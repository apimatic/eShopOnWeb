using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IPayPalGateway
{
    string Currency { get; }

    Task<PayPalAuthorizationResult> AuthorizeCardPaymentAsync(
        string invoiceId,
        string customId,
        decimal amount,
        CardPaymentSource card,
        string idempotencyKey,
        CancellationToken cancellationToken = default);

    Task<PayPalAuthorizationResult> AuthorizeVaultedCardPaymentAsync(
        string invoiceId,
        string customId,
        decimal amount,
        string vaultId,
        string idempotencyKey,
        CancellationToken cancellationToken = default);

    Task<PayPalAuthorizationDetails> GetAuthorizationAsync(
        string authorizationId,
        CancellationToken cancellationToken = default);

    Task<PayPalAuthorizationResult> ReauthorizeAsync(
        string originalAuthorizationId,
        decimal amount,
        string idempotencyKey,
        CancellationToken cancellationToken = default);

    Task<PayPalCaptureResult> CaptureAuthorizationAsync(
        string authorizationId,
        decimal amount,
        string invoiceId,
        string idempotencyKey,
        CancellationToken cancellationToken = default);

    Task VoidAuthorizationAsync(
        string authorizationId,
        CancellationToken cancellationToken = default);

    Task<PayPalRefundResult> RefundCaptureAsync(
        string captureId,
        decimal? amount,
        string currency,
        string idempotencyKey,
        CancellationToken cancellationToken = default);

    Task<PayPalVaultedCard> VaultCardAsync(
        CardPaymentSource card,
        string? payPalCustomerId,
        CancellationToken cancellationToken = default);

    Task DeleteVaultedCardAsync(
        string paymentTokenId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PayPalReportedTransaction>> ListTransactionsAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default);
}

public record CardPaymentSource(
    string Number,
    string Expiry,
    string? SecurityCode,
    string Name,
    CardBillingAddress? BillingAddress);

public record CardBillingAddress(
    string AddressLine1,
    string? AddressLine2,
    string AdminArea2,
    string? AdminArea1,
    string PostalCode,
    string CountryCode);

public record PayPalAuthorizationResult(
    string PayPalOrderId,
    string AuthorizationId,
    string Status,
    decimal Amount,
    string Currency,
    DateTimeOffset AuthorizedAt,
    DateTimeOffset? ExpiresAt);

public record PayPalAuthorizationDetails(
    string AuthorizationId,
    string Status,
    decimal Amount,
    string Currency,
    DateTimeOffset? CreateTime,
    DateTimeOffset? ExpirationTime);

public record PayPalCaptureResult(
    string CaptureId,
    string Status,
    decimal CapturedAmount,
    decimal? PayPalFee,
    decimal? NetAmount,
    string Currency);

public record PayPalRefundResult(
    string RefundId,
    string Status,
    decimal Amount,
    string Currency);

public record PayPalVaultedCard(
    string PaymentTokenId,
    string? CustomerId,
    string Brand,
    string LastFourDigits,
    string? Expiry,
    string? CardholderName);

public record PayPalReportedTransaction(
    string TransactionId,
    string? PayPalReferenceId,
    string? InvoiceId,
    string? CustomField,
    string? EventCode,
    string? Status,
    decimal? Amount,
    decimal? FeeAmount,
    string? Currency,
    DateTimeOffset? InitiationDate);
