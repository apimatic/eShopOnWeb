using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

public sealed class PayPalAuthorizationResult
{
    public PayPalAuthorizationResult(
        string paypalOrderId,
        string authorizationId,
        string status,
        DateTimeOffset? expiresAt,
        decimal amount)
    {
        PayPalOrderId = paypalOrderId;
        AuthorizationId = authorizationId;
        Status = status;
        ExpiresAt = expiresAt;
        Amount = amount;
    }

    public string PayPalOrderId { get; }
    public string AuthorizationId { get; }
    public string Status { get; }
    public DateTimeOffset? ExpiresAt { get; }
    public decimal Amount { get; }
}

public sealed class PayPalAuthorizationDetails
{
    public PayPalAuthorizationDetails(string id, string status, DateTimeOffset? expiresAt, decimal amount)
    {
        Id = id;
        Status = status;
        ExpiresAt = expiresAt;
        Amount = amount;
    }

    public string Id { get; }
    public string Status { get; }
    public DateTimeOffset? ExpiresAt { get; }
    public decimal Amount { get; }
}

public sealed class PayPalCaptureResult
{
    public PayPalCaptureResult(
        string captureId,
        string status,
        decimal capturedAmount,
        decimal paypalFee,
        decimal netAmount)
    {
        CaptureId = captureId;
        Status = status;
        CapturedAmount = capturedAmount;
        PaypalFee = paypalFee;
        NetAmount = netAmount;
    }

    public string CaptureId { get; }
    public string Status { get; }
    public decimal CapturedAmount { get; }
    public decimal PaypalFee { get; }
    public decimal NetAmount { get; }
}

public sealed class PayPalRefundResult
{
    public PayPalRefundResult(string refundId, string status, decimal amount)
    {
        RefundId = refundId;
        Status = status;
        Amount = amount;
    }

    public string RefundId { get; }
    public string Status { get; }
    public decimal Amount { get; }
}

public sealed class PayPalVaultedCardResult
{
    public PayPalVaultedCardResult(
        string paymentTokenId,
        string? paypalCustomerId,
        string? brand,
        string? lastDigits,
        string? expiry,
        string? cardholderName)
    {
        PaymentTokenId = paymentTokenId;
        PayPalCustomerId = paypalCustomerId;
        Brand = brand;
        LastDigits = lastDigits;
        Expiry = expiry;
        CardholderName = cardholderName;
    }

    public string PaymentTokenId { get; }
    public string? PayPalCustomerId { get; }
    public string? Brand { get; }
    public string? LastDigits { get; }
    public string? Expiry { get; }
    public string? CardholderName { get; }
}

public sealed class PayPalReportedTransaction
{
    public PayPalReportedTransaction(
        string? transactionId,
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
        PayPalReferenceId = paypalReferenceId;
        InvoiceId = invoiceId;
        CustomField = customField;
        EventCode = eventCode;
        Status = status;
        Amount = amount;
        Currency = currency;
        InitiationDate = initiationDate;
    }

    public string? TransactionId { get; }
    public string? PayPalReferenceId { get; }
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
        IReadOnlyList<ReconciliationEshopEntry> eshopOnly)
    {
        From = from;
        To = to;
        Matched = matched;
        PayPalOnly = paypalOnly;
        EshopOnly = eshopOnly;
    }

    public DateTimeOffset From { get; }
    public DateTimeOffset To { get; }
    public IReadOnlyList<ReconciliationMatch> Matched { get; }
    public IReadOnlyList<PayPalReportedTransaction> PayPalOnly { get; }
    public IReadOnlyList<ReconciliationEshopEntry> EshopOnly { get; }
}

public sealed class ReconciliationMatch
{
    public ReconciliationMatch(int orderId, PayPalReportedTransaction paypalTransaction)
    {
        OrderId = orderId;
        PayPalTransaction = paypalTransaction;
    }

    public int OrderId { get; }
    public PayPalReportedTransaction PayPalTransaction { get; }
}

public sealed class ReconciliationEshopEntry
{
    public ReconciliationEshopEntry(
        int orderId,
        string status,
        string? paypalOrderId,
        string? authorizationId,
        string? captureId,
        DateTimeOffset orderDate,
        decimal total)
    {
        OrderId = orderId;
        Status = status;
        PayPalOrderId = paypalOrderId;
        AuthorizationId = authorizationId;
        CaptureId = captureId;
        OrderDate = orderDate;
        Total = total;
    }

    public int OrderId { get; }
    public string Status { get; }
    public string? PayPalOrderId { get; }
    public string? AuthorizationId { get; }
    public string? CaptureId { get; }
    public DateTimeOffset OrderDate { get; }
    public decimal Total { get; }
}
