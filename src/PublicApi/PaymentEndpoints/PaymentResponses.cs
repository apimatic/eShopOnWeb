using System;
using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

public class PlaceOrderResponse
{
    public int OrderId { get; set; }
    public OrderSummaryDto Order { get; set; } = new();
}

public class OrderResponse
{
    public OrderSummaryDto Order { get; set; } = new();
}

public class MyOrdersResponse
{
    public List<OrderSummaryDto> Orders { get; set; } = new();
}

public class RefundOrderResponse
{
    public string RefundId { get; set; } = string.Empty;
    public RefundDto Refund { get; set; } = new();
    public OrderSummaryDto Order { get; set; } = new();
}

public class SavePaymentMethodResponse
{
    public int PaymentMethodId { get; set; }
    public SavedCardDto Card { get; set; } = new();
}

public class ListPaymentMethodsResponse
{
    public List<SavedCardDto> PaymentMethods { get; set; } = new();
}

// ----- Reconciliation -----

public class ReconciliationResponse
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public DateTimeOffset GeneratedAt { get; set; }
    public int PayPalTransactionCount { get; set; }
    public int MatchedCount { get; set; }
    public int MissingInEShopCount { get; set; }
    public int MissingInPayPalCount { get; set; }
    public List<ReconciliationLineDto> Lines { get; set; } = new();

    public static ReconciliationResponse From(ReconciliationReport report) => new()
    {
        From = report.From,
        To = report.To,
        GeneratedAt = report.GeneratedAt,
        PayPalTransactionCount = report.PayPalTransactionCount,
        MatchedCount = report.MatchedCount,
        MissingInEShopCount = report.MissingInEShopCount,
        MissingInPayPalCount = report.MissingInPayPalCount,
        Lines = report.Lines.ConvertAll(ReconciliationLineDto.From)
    };
}

public class ReconciliationLineDto
{
    public string Match { get; set; } = string.Empty;
    public string? PayPalTransactionId { get; set; }
    public string? PayPalStatus { get; set; }
    public decimal? PayPalAmount { get; set; }
    public decimal? PayPalFee { get; set; }
    public string? PayPalReference { get; set; }
    public DateTimeOffset? PayPalDate { get; set; }
    public int? OrderId { get; set; }
    public string? OrderStatus { get; set; }
    public decimal? EShopAmount { get; set; }
    public string? OrderReference { get; set; }
    public bool AmountMismatch { get; set; }

    public static ReconciliationLineDto From(ReconciliationLine l) => new()
    {
        Match = l.Match.ToString(),
        PayPalTransactionId = l.PayPalTransactionId,
        PayPalStatus = l.PayPalStatus,
        PayPalAmount = l.PayPalAmount,
        PayPalFee = l.PayPalFee,
        PayPalReference = l.PayPalReference,
        PayPalDate = l.PayPalDate,
        OrderId = l.OrderId,
        OrderStatus = l.OrderStatus,
        EShopAmount = l.EShopAmount,
        OrderReference = l.OrderReference,
        AmountMismatch = l.AmountMismatch
    };
}
