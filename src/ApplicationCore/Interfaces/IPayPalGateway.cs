using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IPayPalGateway
{
    Task<PayPalOrderResult> CreateAuthorizeOrderAsync(
        decimal amount,
        string currency,
        string customId,
        string invoiceId,
        string requestId,
        CancellationToken cancellationToken = default);

    Task<PayPalOrderResult> AuthorizeOrderAsync(
        string payPalOrderId,
        CardPayment? card,
        string? vaultId,
        string requestId,
        CancellationToken cancellationToken = default);

    Task<PayPalAuthorizationResult> GetAuthorizationAsync(
        string authorizationId,
        CancellationToken cancellationToken = default);

    Task<PayPalAuthorizationResult> ReauthorizeAsync(
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
        string invoiceId,
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

    Task<PayPalVaultedCardResult> SaveCardAsync(
        CardPayment card,
        string? payPalCustomerId,
        string requestId,
        CancellationToken cancellationToken = default);

    Task DeleteVaultedCardAsync(
        string vaultId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PayPalTransactionRecord>> ListTransactionsAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default);
}

public sealed class PayPalOrderResult
{
    public string Id { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public PayPalAuthorizationResult? Authorization { get; init; }
    public bool RequiresPayerAction { get; init; }
}

public sealed class PayPalAuthorizationResult
{
    public string Id { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public DateTimeOffset? ExpirationTime { get; init; }
    public DateTimeOffset? CreateTime { get; init; }
    public decimal? Amount { get; init; }
    public string? Currency { get; init; }
}

public sealed class PayPalCaptureResult
{
    public string Id { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public decimal Amount { get; init; }
    public string Currency { get; init; } = string.Empty;
    public decimal? PayPalFee { get; init; }
    public decimal? NetAmount { get; init; }
}

public sealed class PayPalRefundResult
{
    public string Id { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public decimal Amount { get; init; }
    public string Currency { get; init; } = string.Empty;
}

public sealed class PayPalVaultedCardResult
{
    public string VaultId { get; init; } = string.Empty;
    public string? CustomerId { get; init; }
    public string LastDigits { get; init; } = string.Empty;
    public string? Brand { get; init; }
    public string? Expiry { get; init; }
    public string? CardholderName { get; init; }
}

public sealed class PayPalTransactionRecord
{
    public string TransactionId { get; init; } = string.Empty;
    public string? PaypalReferenceId { get; init; }
    public string? InvoiceId { get; init; }
    public string? CustomField { get; init; }
    public string? Status { get; init; }
    public string? EventCode { get; init; }
    public DateTimeOffset? InitiationDate { get; init; }
    public decimal? Amount { get; init; }
    public string? Currency { get; init; }
    public decimal? FeeAmount { get; init; }
}
