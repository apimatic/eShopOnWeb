using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.eShopWeb.ApplicationCore.Entities.SavedCardAggregate;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

public class PlaceOrderResponse : BaseResponse
{
    public PlaceOrderResponse(Guid correlationId) : base(correlationId) { }

    /// <summary>Top-level identifier of the newly placed order.</summary>
    public int OrderId { get; set; }
}

public class PayOrderResponse : BaseResponse
{
    public PayOrderResponse(Guid correlationId) : base(correlationId) { }

    public int OrderId { get; set; }
    public PaymentResponse Payment { get; set; } = new();
}

public class OrderPaymentStateResponse : BaseResponse
{
    public OrderPaymentStateResponse(Guid correlationId) : base(correlationId) { }

    public int OrderId { get; set; }
    public string OrderStatus { get; set; } = string.Empty;
    public PaymentResponse? Payment { get; set; }
}

public class RefundOrderResponse : BaseResponse
{
    public RefundOrderResponse(Guid correlationId) : base(correlationId) { }

    /// <summary>Top-level identifier of the refund that was issued.</summary>
    public string RefundId { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Status { get; set; } = string.Empty;
}

public class OrderSummaryResponse
{
    public int OrderId { get; set; }
    public DateTimeOffset OrderDate { get; set; }
    public decimal Total { get; set; }
    public string OrderStatus { get; set; } = string.Empty;
    public List<OrderLineResponse> Items { get; set; } = new();
    public PaymentResponse? Payment { get; set; }

    public static OrderSummaryResponse From(OrderWithPayment owp) => new()
    {
        OrderId = owp.Order.Id,
        OrderDate = owp.Order.OrderDate,
        Total = owp.Order.Total(),
        OrderStatus = owp.Order.Status.ToString(),
        Items = owp.Order.OrderItems
            .Select(i => new OrderLineResponse
            {
                CatalogItemId = i.ItemOrdered.CatalogItemId,
                ProductName = i.ItemOrdered.ProductName,
                UnitPrice = i.UnitPrice,
                Units = i.Units
            }).ToList(),
        Payment = owp.Payment is null ? null : PaymentResponse.From(owp.Payment)
    };
}

public class OrderLineResponse
{
    public int CatalogItemId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public int Units { get; set; }
}

public class MyOrdersResponse : BaseResponse
{
    public MyOrdersResponse(Guid correlationId) : base(correlationId) { }

    public List<OrderSummaryResponse> Orders { get; set; } = new();
}

public class SavePaymentMethodResponse : BaseResponse
{
    public SavePaymentMethodResponse(Guid correlationId) : base(correlationId) { }

    /// <summary>Top-level identifier of the saved card.</summary>
    public int PaymentMethodId { get; set; }
    public string? Brand { get; set; }
    public string? Last4 { get; set; }
    public string? Expiry { get; set; }
    public string? Alias { get; set; }
}

public class PaymentMethodResponse
{
    public int PaymentMethodId { get; set; }
    public string? Brand { get; set; }
    public string? Last4 { get; set; }
    public string? Expiry { get; set; }
    public string? Alias { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public static PaymentMethodResponse From(SavedCard card) => new()
    {
        PaymentMethodId = card.Id,
        Brand = card.Brand,
        Last4 = card.LastDigits,
        Expiry = card.Expiry,
        Alias = card.Alias,
        CreatedAt = card.CreatedAt
    };
}

public class ListPaymentMethodsResponse : BaseResponse
{
    public ListPaymentMethodsResponse(Guid correlationId) : base(correlationId) { }

    public List<PaymentMethodResponse> PaymentMethods { get; set; } = new();
}

public class ReconciliationLineResponse
{
    public string Outcome { get; set; } = string.Empty;
    public string? PayPalTransactionId { get; set; }
    public string? PayPalStatus { get; set; }
    public decimal? PayPalAmount { get; set; }
    public string? Currency { get; set; }
    public DateTimeOffset? TransactionDate { get; set; }
    public int? OrderId { get; set; }
    public decimal? OrderCapturedAmount { get; set; }
    public string? CaptureId { get; set; }
}

public class ReconciliationResponse : BaseResponse
{
    public ReconciliationResponse(Guid correlationId) : base(correlationId) { }

    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public int PayPalTransactionCount { get; set; }
    public int MatchedCount { get; set; }
    public List<ReconciliationLineResponse> Lines { get; set; } = new();

    public static ReconciliationResponse Create(Guid correlationId, ReconciliationReport report)
    {
        return new ReconciliationResponse(correlationId)
        {
            From = report.From,
            To = report.To,
            PayPalTransactionCount = report.PayPalTransactionCount,
            MatchedCount = report.MatchedCount,
            Lines = report.Lines.Select(l => new ReconciliationLineResponse
            {
                Outcome = l.Outcome.ToString(),
                PayPalTransactionId = l.PayPalTransactionId,
                PayPalStatus = l.PayPalStatus,
                PayPalAmount = l.PayPalAmount,
                Currency = l.Currency,
                TransactionDate = l.TransactionDate,
                OrderId = l.OrderId,
                OrderCapturedAmount = l.OrderCapturedAmount,
                CaptureId = l.CaptureId
            }).ToList()
        };
    }
}
