using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IPayPalClient
{
    string Currency { get; }

    Task<PayPalAuthorizationResult> AuthorizeOrderAsync(
        PayPalAuthorizeRequest request,
        string requestId,
        CancellationToken cancellationToken = default);

    Task<PayPalAuthorizationDetails> GetAuthorizationAsync(
        string authorizationId,
        CancellationToken cancellationToken = default);

    Task<PayPalAuthorizationDetails> ReauthorizeAsync(
        string authorizationId,
        decimal amount,
        string currency,
        string requestId,
        CancellationToken cancellationToken = default);

    Task VoidAuthorizationAsync(
        string authorizationId,
        string requestId,
        CancellationToken cancellationToken = default);

    Task<PayPalCaptureResult> CaptureAuthorizationAsync(
        string authorizationId,
        decimal amount,
        string currency,
        string requestId,
        CancellationToken cancellationToken = default);

    Task<PayPalCaptureResult> GetCaptureAsync(
        string captureId,
        CancellationToken cancellationToken = default);

    Task<PayPalRefundResult> RefundCaptureAsync(
        string captureId,
        decimal? amount,
        string currency,
        string requestId,
        CancellationToken cancellationToken = default);

    Task<PayPalVaultedCard> CreatePaymentTokenAsync(
        string customerId,
        CardPaymentDetails card,
        string requestId,
        CancellationToken cancellationToken = default);

    Task DeletePaymentTokenAsync(
        string vaultId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PayPalReportedTransaction>> ListTransactionsAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default);
}

public sealed class PayPalAuthorizeRequest
{
    public required decimal Amount { get; init; }
    public required string Currency { get; init; }
    public required string InvoiceId { get; init; }
    public required string CustomId { get; init; }
    public CardPaymentDetails? Card { get; init; }
    public string? VaultId { get; init; }
}

public sealed class PayPalAuthorizationResult
{
    public required string PayPalOrderId { get; init; }
    public required string OrderStatus { get; init; }
    public required string AuthorizationId { get; init; }
    public required string AuthorizationStatus { get; init; }
    public DateTimeOffset? Expiration { get; init; }
    public DateTimeOffset AuthorizedAt { get; init; }
    public string? Last4 { get; init; }
    public string? Brand { get; init; }
}

public sealed class PayPalAuthorizationDetails
{
    public required string AuthorizationId { get; init; }
    public required string Status { get; init; }
    public DateTimeOffset? Expiration { get; init; }
    public DateTimeOffset? CreateTime { get; init; }
    public decimal? Amount { get; init; }
    public string? Currency { get; init; }
}

public sealed class PayPalCaptureResult
{
    public required string CaptureId { get; init; }
    public required string Status { get; init; }
    public required decimal GrossAmount { get; init; }
    public decimal? PayPalFee { get; init; }
    public decimal? NetAmount { get; init; }
    public required string Currency { get; init; }
    public DateTimeOffset CapturedAt { get; init; }
}

public sealed class PayPalRefundResult
{
    public required string RefundId { get; init; }
    public required string Status { get; init; }
    public required decimal Amount { get; init; }
    public required string Currency { get; init; }
}

public sealed class PayPalVaultedCard
{
    public required string VaultId { get; init; }
    public string? Last4 { get; init; }
    public string? Brand { get; init; }
    public string? Expiry { get; init; }
    public string? Name { get; init; }
    public string? PayPalCustomerId { get; init; }
}

public sealed class PayPalReportedTransaction
{
    public required string TransactionId { get; init; }
    public string? ReferenceId { get; init; }
    public string? InvoiceId { get; init; }
    public string? CustomField { get; init; }
    public string? EventCode { get; init; }
    public string? Status { get; init; }
    public decimal? Amount { get; init; }
    public string? Currency { get; init; }
    public decimal? Fee { get; init; }
    public DateTimeOffset? InitiationDate { get; init; }
    public string? InstrumentType { get; init; }
}
