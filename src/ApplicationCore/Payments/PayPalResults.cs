using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

public sealed class PayPalPurchaseLine
{
    public string Name { get; init; } = string.Empty;
    public decimal UnitAmount { get; init; }
    public int Quantity { get; init; }
}

public sealed class PayPalAuthorizationResult
{
    public string PayPalOrderId { get; init; } = string.Empty;
    public string AuthorizationId { get; init; } = string.Empty;
    public string AuthorizationStatus { get; init; } = string.Empty;
    public decimal Amount { get; init; }
    public string Currency { get; init; } = string.Empty;
    public DateTimeOffset? Expiration { get; init; }
}

public sealed class PayPalCaptureResult
{
    public string CaptureId { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public decimal CapturedAmount { get; init; }
    public decimal PaypalFee { get; init; }
    public decimal NetProceeds { get; init; }
    public string Currency { get; init; } = string.Empty;
}

public sealed class PayPalRefundResult
{
    public string RefundId { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public decimal Amount { get; init; }
    public string Currency { get; init; } = string.Empty;
}

public sealed class PayPalVaultedCard
{
    public string PaymentTokenId { get; init; } = string.Empty;
    public string? CustomerId { get; init; }
    public string LastDigits { get; init; } = string.Empty;
    public string? Brand { get; init; }
    public string? Expiry { get; init; }
    public string? CardholderName { get; init; }
}

public sealed class PayPalAuthorizationDetails
{
    public string AuthorizationId { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public decimal Amount { get; init; }
    public string Currency { get; init; } = string.Empty;
    public DateTimeOffset? Expiration { get; init; }
    public DateTimeOffset? CreateTime { get; init; }
}

public sealed class PayPalReportedTransaction
{
    public string TransactionId { get; init; } = string.Empty;
    public string? ReferenceId { get; init; }
    public string? InvoiceId { get; init; }
    public string? CustomField { get; init; }
    public string? EventCode { get; init; }
    public string? Status { get; init; }
    public decimal Amount { get; init; }
    public string? Currency { get; init; }
    public DateTimeOffset? InitiationDate { get; init; }
}

public sealed class ReconciliationReport
{
    public DateTimeOffset From { get; init; }
    public DateTimeOffset To { get; init; }
    public IReadOnlyList<ReconciledRow> Matches { get; init; } = Array.Empty<ReconciledRow>();
    public IReadOnlyList<PayPalReportedTransaction> PayPalOnly { get; init; } = Array.Empty<PayPalReportedTransaction>();
    public IReadOnlyList<EshopUnmatchedOrder> EshopOnly { get; init; } = Array.Empty<EshopUnmatchedOrder>();
}

public sealed class ReconciledRow
{
    public int OrderId { get; init; }
    public string MatchReason { get; init; } = string.Empty;
    public PayPalReportedTransaction PayPalTransaction { get; init; } = new();
}

public sealed class EshopUnmatchedOrder
{
    public int OrderId { get; init; }
    public string Status { get; init; } = string.Empty;
    public string? PayPalOrderId { get; init; }
    public string? PayPalAuthorizationId { get; init; }
    public string? PayPalCaptureId { get; init; }
    public decimal Total { get; init; }
    public DateTimeOffset OrderDate { get; init; }
}
