using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

public sealed class PayPalAuthorizationResult
{
    public PayPalAuthorizationResult(
        string payPalOrderId,
        string authorizationId,
        string status,
        decimal amount,
        string currency,
        DateTimeOffset? expirationTime,
        DateTimeOffset? createTime)
    {
        PayPalOrderId = payPalOrderId;
        AuthorizationId = authorizationId;
        Status = status;
        Amount = amount;
        Currency = currency;
        ExpirationTime = expirationTime;
        CreateTime = createTime;
    }

    public string PayPalOrderId { get; }
    public string AuthorizationId { get; }
    public string Status { get; }
    public decimal Amount { get; }
    public string Currency { get; }
    public DateTimeOffset? ExpirationTime { get; }
    public DateTimeOffset? CreateTime { get; }
}

public sealed class PayPalAuthorizationDetails
{
    public PayPalAuthorizationDetails(
        string authorizationId,
        string status,
        decimal amount,
        string currency,
        DateTimeOffset? expirationTime,
        DateTimeOffset? createTime)
    {
        AuthorizationId = authorizationId;
        Status = status;
        Amount = amount;
        Currency = currency;
        ExpirationTime = expirationTime;
        CreateTime = createTime;
    }

    public string AuthorizationId { get; }
    public string Status { get; }
    public decimal Amount { get; }
    public string Currency { get; }
    public DateTimeOffset? ExpirationTime { get; }
    public DateTimeOffset? CreateTime { get; }
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

public sealed class PayPalVaultedCard
{
    public PayPalVaultedCard(
        string paymentTokenId,
        string last4,
        string brand,
        string expiry,
        string? cardholderName,
        string? customerId)
    {
        PaymentTokenId = paymentTokenId;
        Last4 = last4;
        Brand = brand;
        Expiry = expiry;
        CardholderName = cardholderName;
        CustomerId = customerId;
    }

    public string PaymentTokenId { get; }
    public string Last4 { get; }
    public string Brand { get; }
    public string Expiry { get; }
    public string? CardholderName { get; }
    public string? CustomerId { get; }
}

public sealed class PayPalReportedTransaction
{
    public PayPalReportedTransaction(
        string transactionId,
        string? paypalReferenceId,
        string? invoiceId,
        string? customField,
        string? eventCode,
        string? status,
        decimal? amount,
        string? currency,
        DateTimeOffset? initiationDate)
    {
        TransactionId = transactionId;
        PaypalReferenceId = paypalReferenceId;
        InvoiceId = invoiceId;
        CustomField = customField;
        EventCode = eventCode;
        Status = status;
        Amount = amount;
        Currency = currency;
        InitiationDate = initiationDate;
    }

    public string TransactionId { get; }
    public string? PaypalReferenceId { get; }
    public string? InvoiceId { get; }
    public string? CustomField { get; }
    public string? EventCode { get; }
    public string? Status { get; }
    public decimal? Amount { get; }
    public string? Currency { get; }
    public DateTimeOffset? InitiationDate { get; }
}

public sealed class ReconciliationReport
{
    public ReconciliationReport(
        DateTimeOffset from,
        DateTimeOffset to,
        IReadOnlyList<ReconciliationMatch> matched,
        IReadOnlyList<PayPalReportedTransaction> paypalOnly,
        IReadOnlyList<EshopPaymentRecord> eshopOnly)
    {
        From = from;
        To = to;
        Matched = matched;
        PaypalOnly = paypalOnly;
        EshopOnly = eshopOnly;
    }

    public DateTimeOffset From { get; }
    public DateTimeOffset To { get; }
    public IReadOnlyList<ReconciliationMatch> Matched { get; }
    public IReadOnlyList<PayPalReportedTransaction> PaypalOnly { get; }
    public IReadOnlyList<EshopPaymentRecord> EshopOnly { get; }
}

public sealed class ReconciliationMatch
{
    public ReconciliationMatch(EshopPaymentRecord order, PayPalReportedTransaction transaction)
    {
        Order = order;
        Transaction = transaction;
    }

    public EshopPaymentRecord Order { get; }
    public PayPalReportedTransaction Transaction { get; }
}

public sealed class EshopPaymentRecord
{
    public EshopPaymentRecord(
        int orderId,
        string status,
        string? payPalOrderId,
        string? invoiceId,
        string? authorizationId,
        string? captureId,
        IReadOnlyList<string> refundIds,
        DateTimeOffset orderDate)
    {
        OrderId = orderId;
        Status = status;
        PayPalOrderId = payPalOrderId;
        InvoiceId = invoiceId;
        AuthorizationId = authorizationId;
        CaptureId = captureId;
        RefundIds = refundIds;
        OrderDate = orderDate;
    }

    public int OrderId { get; }
    public string Status { get; }
    public string? PayPalOrderId { get; }
    public string? InvoiceId { get; }
    public string? AuthorizationId { get; }
    public string? CaptureId { get; }
    public IReadOnlyList<string> RefundIds { get; }
    public DateTimeOffset OrderDate { get; }
}
