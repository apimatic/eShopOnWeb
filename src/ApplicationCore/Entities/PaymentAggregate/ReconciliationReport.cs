using System;
using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.PaymentGateway;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// Lines up the processor's own record of transactions for a date range against this application's
/// payments, so money that one side knows about and the other does not is visible.
/// </summary>
public class ReconciliationReport
{
    public DateTimeOffset From { get; init; }
    public DateTimeOffset To { get; init; }
    public DateTimeOffset Generated { get; init; }
    public string Currency { get; init; } = string.Empty;

    /// <summary>Every line the processor reported for the range, with what it matched to.</summary>
    public IReadOnlyList<ReconciliationLine> PayPalTransactions { get; init; } = new List<ReconciliationLine>();

    /// <summary>This application's payments for the range, with what the processor reported.</summary>
    public IReadOnlyList<ReconciliationPayment> EshopPayments { get; init; } = new List<ReconciliationPayment>();

    public ReconciliationSummary Summary { get; init; } = new ReconciliationSummary();
}

public class ReconciliationLine
{
    public required ProcessorTransactionLine Transaction { get; init; }
    public int? EshopOrderId { get; init; }
    public int? EshopPaymentId { get; init; }
    public bool KnownToEshop { get; init; }
}

public class ReconciliationPayment
{
    public required int PaymentId { get; init; }
    public required int OrderId { get; init; }
    public required string PaymentStatus { get; init; }
    public decimal AuthorizedAmount { get; init; }
    public decimal CapturedAmount { get; init; }
    public decimal FeeAmount { get; init; }
    public decimal NetAmount { get; init; }
    public decimal RefundedAmount { get; init; }
    public decimal RefundableAmount { get; init; }
    public string? PayPalOrderId { get; init; }
    public string? AuthorizationId { get; init; }
    public string? CaptureId { get; init; }
    public IReadOnlyList<string> RefundIds { get; init; } = new List<string>();
    public bool SeenInPayPalRecord { get; init; }
    public IReadOnlyList<string> Issues { get; init; } = new List<string>();
}

public class ReconciliationSummary
{
    public int PayPalTransactionCount { get; init; }
    public int EshopPaymentCount { get; init; }
    public int MatchedCount { get; init; }

    /// <summary>Lines the processor reported that no eShop payment accounts for.</summary>
    public int OnlyInPayPalCount { get; init; }

    /// <summary>eShop payments with no matching line in the processor's record for the range.</summary>
    public int OnlyInEshopCount { get; init; }

    public decimal PayPalGrossAmount { get; init; }
    public decimal PayPalFeesAmount { get; init; }
    public decimal EshopCapturedAmount { get; init; }
    public decimal EshopRefundedAmount { get; init; }
}
