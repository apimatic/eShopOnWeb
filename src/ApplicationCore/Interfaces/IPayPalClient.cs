using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IPayPalClient
{
    Task<PayPalOrderResult> CreateAuthorizedOrderAsync(
        string payPalRequestId,
        decimal amount,
        string currency,
        string customId,
        string invoiceId,
        CardDetails? card,
        string? vaultId,
        CancellationToken cancellationToken = default);

    Task<PayPalOrderResult> GetOrderAsync(string orderId, CancellationToken cancellationToken = default);

    Task<PayPalOrderResult> AuthorizeOrderAsync(
        string orderId,
        string payPalRequestId,
        CancellationToken cancellationToken = default);

    Task<PayPalAuthorizationResult> GetAuthorizationAsync(
        string authorizationId,
        CancellationToken cancellationToken = default);

    Task<PayPalAuthorizationResult> ReauthorizeAsync(
        string authorizationId,
        string payPalRequestId,
        decimal amount,
        string currency,
        CancellationToken cancellationToken = default);

    Task VoidAuthorizationAsync(
        string authorizationId,
        string payPalRequestId,
        CancellationToken cancellationToken = default);

    Task<PayPalCaptureResult> CaptureAuthorizationAsync(
        string authorizationId,
        string payPalRequestId,
        decimal amount,
        string currency,
        CancellationToken cancellationToken = default);

    Task<PayPalCaptureResult> GetCaptureAsync(string captureId, CancellationToken cancellationToken = default);

    Task<PayPalRefundResult> RefundCaptureAsync(
        string captureId,
        string payPalRequestId,
        decimal amount,
        string currency,
        CancellationToken cancellationToken = default);

    Task<PayPalPaymentTokenResult> CreatePaymentTokenAsync(
        string payPalRequestId,
        string customerId,
        string merchantCustomerId,
        CardDetails card,
        CancellationToken cancellationToken = default);

    Task DeletePaymentTokenAsync(string paymentTokenId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PayPalReportedTransaction>> ListTransactionsAsync(
        DateTimeOffset start,
        DateTimeOffset end,
        CancellationToken cancellationToken = default);
}

public sealed class PayPalOrderResult
{
    public required string Id { get; init; }
    public required string Status { get; init; }
    public string? AuthorizationId { get; init; }
    public string? AuthorizationStatus { get; init; }
    public decimal? AuthorizedAmount { get; init; }
    public string? Currency { get; init; }
    public DateTimeOffset? AuthorizationExpiration { get; init; }
    public bool PayerActionRequired { get; init; }
}

public sealed class PayPalAuthorizationResult
{
    public required string Id { get; init; }
    public required string Status { get; init; }
    public decimal? Amount { get; init; }
    public string? Currency { get; init; }
    public DateTimeOffset? ExpirationTime { get; init; }
    public DateTimeOffset? CreateTime { get; init; }
}

public sealed class PayPalCaptureResult
{
    public required string Id { get; init; }
    public required string Status { get; init; }
    public decimal? Amount { get; init; }
    public decimal? PayPalFee { get; init; }
    public decimal? NetAmount { get; init; }
    public string? Currency { get; init; }
    public string? AuthorizationId { get; init; }
}

public sealed class PayPalRefundResult
{
    public required string Id { get; init; }
    public required string Status { get; init; }
    public decimal? Amount { get; init; }
    public string? Currency { get; init; }
}

public sealed class PayPalPaymentTokenResult
{
    public required string Id { get; init; }
    public string? CustomerId { get; init; }
    public string? Brand { get; init; }
    public string? LastDigits { get; init; }
    public string? Expiry { get; init; }
    public string? Name { get; init; }
}

public sealed class PayPalReportedTransaction
{
    public string? TransactionId { get; init; }
    public string? PayPalReferenceId { get; init; }
    public string? CustomField { get; init; }
    public string? InvoiceId { get; init; }
    public string? TransactionEventCode { get; init; }
    public string? TransactionStatus { get; init; }
    public string? Amount { get; init; }
    public string? Currency { get; init; }
    public DateTimeOffset? InitiationDate { get; init; }
}
