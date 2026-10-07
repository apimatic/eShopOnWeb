using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class ListMyOrdersRequest : BaseRequest
{
    public ListMyOrdersRequest(string buyerId)
    {
        BuyerId = buyerId;
    }

    public string BuyerId { get; }
}

public class ListMyOrdersResponse : BaseResponse
{
    public ListMyOrdersResponse(Guid correlationId) : base(correlationId)
    {
    }

    public ListMyOrdersResponse()
    {
    }

    public List<MyOrderDto> Orders { get; set; } = new();
}

public class MyOrderDto
{
    public int OrderId { get; set; }
    public DateTimeOffset OrderDate { get; set; }
    public decimal Total { get; set; }
    public string Currency { get; set; } = "";

    /// <summary>AwaitingPayment, PaymentProcessing, Paid, PartiallyRefunded or Refunded.</summary>
    public string PaymentStatus { get; set; } = "";

    public decimal AmountPaid { get; set; }
    public decimal AmountRefunded { get; set; }
    public List<MyOrderItemDto> Items { get; set; } = new();

    /// <summary>The most recent payment attempt, so a shopper can see why an order is still unpaid.</summary>
    public MyOrderPaymentDto? LatestPayment { get; set; }

    public List<MyOrderRefundDto> Refunds { get; set; } = new();
}

public class MyOrderItemDto
{
    public int CatalogItemId { get; set; }
    public string ProductName { get; set; } = "";
    public decimal UnitPrice { get; set; }
    public int Units { get; set; }
}

public class MyOrderPaymentDto
{
    public string Status { get; set; } = "";
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "";
    public string? PspReference { get; set; }
    public string? RefusalReason { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public class MyOrderRefundDto
{
    public Guid RefundId { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "";
    public string Status { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
}
