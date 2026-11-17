using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class CreateOrderResponse : BaseResponse
{
    public CreateOrderResponse(Guid correlationId) : base(correlationId) { }
    public CreateOrderResponse() { }

    public int OrderId { get; set; }
    public OrderPaymentDto Order { get; set; } = new();
}

public class PayOrderResponse : BaseResponse
{
    public PayOrderResponse(Guid correlationId) : base(correlationId) { }
    public PayOrderResponse() { }

    public int OrderId { get; set; }
    public OrderPaymentDto Order { get; set; } = new();
}

public class FulfilOrderResponse : BaseResponse
{
    public FulfilOrderResponse(Guid correlationId) : base(correlationId) { }
    public FulfilOrderResponse() { }

    public int OrderId { get; set; }
    public OrderPaymentDto Order { get; set; } = new();
}

public class CancelOrderResponse : BaseResponse
{
    public CancelOrderResponse(Guid correlationId) : base(correlationId) { }
    public CancelOrderResponse() { }

    public int OrderId { get; set; }
    public OrderPaymentDto Order { get; set; } = new();
}

public class RefundOrderResponse : BaseResponse
{
    public RefundOrderResponse(Guid correlationId) : base(correlationId) { }
    public RefundOrderResponse() { }

    public int OrderId { get; set; }
    public string RefundId { get; set; } = string.Empty;
    public RefundDto Refund { get; set; } = new();
    public OrderPaymentDto Order { get; set; } = new();
}

public class MyOrdersResponse : BaseResponse
{
    public MyOrdersResponse(Guid correlationId) : base(correlationId) { }
    public MyOrdersResponse() { }

    public List<OrderPaymentDto> Orders { get; set; } = new();
}

public class ReconciliationResponse : BaseResponse
{
    public ReconciliationResponse(Guid correlationId) : base(correlationId) { }
    public ReconciliationResponse() { }

    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public List<ReconciliationFinixTransactionDto> FinixTransactions { get; set; } = new();
    public List<ReconciliationOrderDto> EshopOrders { get; set; } = new();
    public List<string> UnmatchedFinixTransactionIds { get; set; } = new();
    public List<int> UnmatchedEshopOrderIds { get; set; } = new();
}
