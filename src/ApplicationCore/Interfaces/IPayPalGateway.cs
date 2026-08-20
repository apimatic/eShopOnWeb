using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IPayPalGateway
{
    string Currency { get; }

    Task<PayPalAuthorizationResult> AuthorizeAsync(
        int orderId,
        decimal amount,
        CardPaymentDetails? card,
        string? vaultId,
        string requestId,
        CancellationToken cancellationToken = default);

    Task<PayPalAuthorizationDetails> GetAuthorizationAsync(string authorizationId, CancellationToken cancellationToken = default);

    Task<PayPalAuthorizationResult> ReauthorizeAsync(
        string authorizationId,
        decimal amount,
        string requestId,
        CancellationToken cancellationToken = default);

    Task<PayPalCaptureResult> CaptureAsync(
        string authorizationId,
        decimal amount,
        string requestId,
        CancellationToken cancellationToken = default);

    Task VoidAsync(string authorizationId, string requestId, CancellationToken cancellationToken = default);

    Task<PayPalRefundResult> RefundAsync(
        string captureId,
        decimal? amount,
        string currency,
        string requestId,
        CancellationToken cancellationToken = default);

    Task<PayPalVaultResult> VaultCardAsync(
        string paypalCustomerId,
        CardPaymentDetails card,
        string requestId,
        CancellationToken cancellationToken = default);

    Task DeleteVaultedCardAsync(string paymentTokenId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PayPalReportedTransaction>> ListTransactionsAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default);
}

public sealed class PayPalAuthorizationResult
{
    public PayPalAuthorizationResult(
        string payPalOrderId,
        string authorizationId,
        string status,
        DateTimeOffset? expiration,
        string amount,
        string currency)
    {
        PayPalOrderId = payPalOrderId;
        AuthorizationId = authorizationId;
        Status = status;
        Expiration = expiration;
        Amount = amount;
        Currency = currency;
    }

    public string PayPalOrderId { get; }
    public string AuthorizationId { get; }
    public string Status { get; }
    public DateTimeOffset? Expiration { get; }
    public string Amount { get; }
    public string Currency { get; }
}

public sealed class PayPalAuthorizationDetails
{
    public PayPalAuthorizationDetails(string id, string status, DateTimeOffset? expiration, string? amount, string? currency)
    {
        Id = id;
        Status = status;
        Expiration = expiration;
        Amount = amount;
        Currency = currency;
    }

    public string Id { get; }
    public string Status { get; }
    public DateTimeOffset? Expiration { get; }
    public string? Amount { get; }
    public string? Currency { get; }
}

public sealed class PayPalCaptureResult
{
    public PayPalCaptureResult(
        string captureId,
        string status,
        decimal capturedAmount,
        decimal? paypalFee,
        decimal? netAmount,
        string currency)
    {
        CaptureId = captureId;
        Status = status;
        CapturedAmount = capturedAmount;
        PaypalFee = paypalFee;
        NetAmount = netAmount;
        Currency = currency;
    }

    public string CaptureId { get; }
    public string Status { get; }
    public decimal CapturedAmount { get; }
    public decimal? PaypalFee { get; }
    public decimal? NetAmount { get; }
    public string Currency { get; }
}

public sealed class PayPalRefundResult
{
    public PayPalRefundResult(string refundId, string status, decimal amount, string currency)
    {
        RefundId = refundId;
        Status = status;
        Amount = amount;
        Currency = currency;
    }

    public string RefundId { get; }
    public string Status { get; }
    public decimal Amount { get; }
    public string Currency { get; }
}

public sealed class PayPalVaultResult
{
    public PayPalVaultResult(string paymentTokenId, string? brand, string? lastDigits, string? expiry, string? cardholderName)
    {
        PaymentTokenId = paymentTokenId;
        Brand = brand;
        LastDigits = lastDigits;
        Expiry = expiry;
        CardholderName = cardholderName;
    }

    public string PaymentTokenId { get; }
    public string? Brand { get; }
    public string? LastDigits { get; }
    public string? Expiry { get; }
    public string? CardholderName { get; }
}

public sealed class PayPalReportedTransaction
{
    public PayPalReportedTransaction(
        string transactionId,
        string? paypalReferenceId,
        string? eventCode,
        string? status,
        string? amount,
        string? currency,
        string? invoiceId,
        string? customField,
        DateTimeOffset? initiationTime)
    {
        TransactionId = transactionId;
        PaypalReferenceId = paypalReferenceId;
        EventCode = eventCode;
        Status = status;
        Amount = amount;
        Currency = currency;
        InvoiceId = invoiceId;
        CustomField = customField;
        InitiationTime = initiationTime;
    }

    public string TransactionId { get; }
    public string? PaypalReferenceId { get; }
    public string? EventCode { get; }
    public string? Status { get; }
    public string? Amount { get; }
    public string? Currency { get; }
    public string? InvoiceId { get; }
    public string? CustomField { get; }
    public DateTimeOffset? InitiationTime { get; }
}

public interface IOrderPaymentService
{
    Task<Order> CreateOrderAsync(string buyerId, IReadOnlyList<OrderLineRequest> items, Address? shipTo, CancellationToken cancellationToken = default);
    Task<Order> PayAsync(int orderId, string buyerId, CardPaymentDetails? card, int? paymentMethodId, CancellationToken cancellationToken = default);
    Task<Order> FulfilAsync(int orderId, CancellationToken cancellationToken = default);
    Task<Order> CancelAsync(int orderId, CancellationToken cancellationToken = default);
    Task<OrderRefund> RefundAsync(int orderId, string buyerId, decimal? amount, string idempotencyKey, bool isAdministrator, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Order>> ListMyOrdersAsync(string buyerId, CancellationToken cancellationToken = default);
    Task<Order> GetMyOrderAsync(int orderId, string buyerId, CancellationToken cancellationToken = default);

    Task<SavedPaymentMethod> SaveCardAsync(string buyerId, CardPaymentDetails card, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SavedPaymentMethod>> ListSavedCardsAsync(string buyerId, CancellationToken cancellationToken = default);
    Task DeleteSavedCardAsync(string buyerId, int paymentMethodId, CancellationToken cancellationToken = default);

    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}

public sealed class OrderLineRequest
{
    public OrderLineRequest(int catalogItemId, int quantity)
    {
        CatalogItemId = catalogItemId;
        Quantity = quantity;
    }

    public int CatalogItemId { get; }
    public int Quantity { get; }
}

public sealed class ReconciliationReport
{
    public ReconciliationReport(
        DateTimeOffset from,
        DateTimeOffset to,
        IReadOnlyList<PayPalReportedTransaction> paypalTransactions,
        IReadOnlyList<Order> localOrders,
        IReadOnlyList<ReconciliationMismatch> mismatches)
    {
        From = from;
        To = to;
        PaypalTransactions = paypalTransactions;
        LocalOrders = localOrders;
        Mismatches = mismatches;
    }

    public DateTimeOffset From { get; }
    public DateTimeOffset To { get; }
    public IReadOnlyList<PayPalReportedTransaction> PaypalTransactions { get; }
    public IReadOnlyList<Order> LocalOrders { get; }
    public IReadOnlyList<ReconciliationMismatch> Mismatches { get; }
}

public sealed class ReconciliationMismatch
{
    public ReconciliationMismatch(string kind, string identifier, string detail)
    {
        Kind = kind;
        Identifier = identifier;
        Detail = detail;
    }

    public string Kind { get; }
    public string Identifier { get; }
    public string Detail { get; }
}
