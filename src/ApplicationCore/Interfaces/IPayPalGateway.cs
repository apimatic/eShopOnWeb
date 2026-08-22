using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.PayPal;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IPayPalGateway
{
    string Currency { get; }

    Task<PayPalAuthorizationResult> AuthorizeAsync(
        decimal amount,
        string currency,
        string invoiceId,
        string customId,
        string idempotencyKey,
        CardPaymentSource? card,
        string? vaultId,
        CancellationToken cancellationToken = default);

    Task<PayPalAuthorizationResult> GetAuthorizationAsync(
        string authorizationId,
        CancellationToken cancellationToken = default);

    Task<PayPalAuthorizationResult> ReauthorizeAsync(
        string authorizationId,
        decimal amount,
        string currency,
        CancellationToken cancellationToken = default);

    Task<PayPalCaptureResult> CaptureAsync(
        string authorizationId,
        string invoiceId,
        string idempotencyKey,
        CancellationToken cancellationToken = default);

    Task<PayPalCaptureResult> GetCaptureAsync(
        string captureId,
        CancellationToken cancellationToken = default);

    Task VoidAuthorizationAsync(
        string authorizationId,
        CancellationToken cancellationToken = default);

    Task<PayPalRefundResult> RefundAsync(
        string captureId,
        decimal? amount,
        string currency,
        string idempotencyKey,
        CancellationToken cancellationToken = default);

    Task<PayPalVaultedCard> VaultCardAsync(
        CardPaymentSource card,
        string? payPalCustomerId,
        string idempotencyKey,
        CancellationToken cancellationToken = default);

    Task DeleteVaultedCardAsync(
        string vaultId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PayPalReportedTransaction>> ListTransactionsAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default);
}

public sealed record PayPalAuthorizationResult(
    string? PayPalOrderId,
    string AuthorizationId,
    string Status,
    DateTimeOffset? ExpirationTime,
    decimal Amount,
    string Currency);

public sealed record PayPalCaptureResult(
    string CaptureId,
    string Status,
    decimal CapturedAmount,
    decimal PayPalFee,
    decimal NetAmount,
    string Currency,
    string? AuthorizationId);

public sealed record PayPalRefundResult(
    string RefundId,
    string Status,
    decimal Amount,
    string Currency);

public sealed record PayPalVaultedCard(
    string VaultId,
    string? CustomerId,
    string LastDigits,
    string Brand,
    string Expiry,
    string? Name);

public sealed record PayPalReportedTransaction(
    string TransactionId,
    string? ReferenceId,
    string? InvoiceId,
    string? CustomField,
    string? EventCode,
    string? Status,
    decimal? Amount,
    string? Currency,
    DateTimeOffset? InitiationDate,
    decimal? Fee);
