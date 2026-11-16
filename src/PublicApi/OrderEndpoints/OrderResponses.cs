using System;
using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class CreateOrderResponse
{
    public int OrderId { get; set; }
    public OrderDto Order { get; set; } = new();
}

public class PayOrderResponse
{
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public PaymentDto Payment { get; set; } = new();
    public OrderDto Order { get; set; } = new();
}

public class FulfilOrderResponse
{
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public long? CapturedAmountCents { get; set; }
    public decimal? CapturedAmount => CapturedAmountCents.HasValue ? CapturedAmountCents.Value / 100m : null;
    public long? FinixFeeCents { get; set; }
    public decimal? FinixFee => FinixFeeCents.HasValue ? FinixFeeCents.Value / 100m : null;
    public long? NetProceedsCents { get; set; }
    public decimal? NetProceeds => NetProceedsCents.HasValue ? NetProceedsCents.Value / 100m : null;
    public PaymentDto Payment { get; set; } = new();
    public OrderDto Order { get; set; } = new();
}

public class CancelOrderResponse
{
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public PaymentDto Payment { get; set; } = new();
    public OrderDto Order { get; set; } = new();
}

public class RefundOrderResponse
{
    public string RefundId { get; set; } = string.Empty;
    public RefundDto Refund { get; set; } = new();
    public OrderDto Order { get; set; } = new();
}

public class MyOrdersResponse
{
    public List<OrderDto> Orders { get; set; } = new();
}

public class ReconciliationResponse
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public List<ReconciliationFinixTransaction> FinixTransactions { get; set; } = new();
    public List<ReconciliationOrder> Orders { get; set; } = new();
    public List<ReconciliationFinixTransaction> UnmatchedFinixTransactions { get; set; } = new();
    public List<ReconciliationOrder> UnmatchedOrders { get; set; } = new();
}
