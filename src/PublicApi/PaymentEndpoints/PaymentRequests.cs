using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

// ----- POST /api/orders -----

public class CreateOrderRequest : BaseRequest
{
    public List<CreateOrderItem> Items { get; set; } = new();
    public AddressDto? ShipToAddress { get; set; }

    /// <summary>Caller identity, set from the token — never from the request body.</summary>
    [JsonIgnore]
    public string BuyerId { get; set; } = string.Empty;
}

public class CreateOrderItem
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

public class CreateOrderResponse : BaseResponse
{
    public CreateOrderResponse(Guid correlationId) : base(correlationId) { }
    public CreateOrderResponse() { }

    public int OrderId { get; set; }
    public OrderDto? Order { get; set; }
}

// ----- POST /api/orders/{orderId}/pay -----

public class PayOrderRequest : BaseRequest
{
    /// <summary>Card details for a one-off payment. Mutually exclusive with <see cref="SavedPaymentMethodId"/>.</summary>
    public CardRequestDto? Card { get; set; }

    /// <summary>Id of one of the caller's saved cards to pay with instead of raw card details.</summary>
    public int? SavedPaymentMethodId { get; set; }

    [JsonIgnore]
    public int OrderId { get; set; }

    [JsonIgnore]
    public string BuyerId { get; set; } = string.Empty;
}

public class PayOrderResponse : BaseResponse
{
    public PayOrderResponse(Guid correlationId) : base(correlationId) { }
    public PayOrderResponse() { }

    public int OrderId { get; set; }
    public OrderDto? Order { get; set; }
}

// ----- POST /api/orders/{orderId}/fulfil | cancel -----

public class OrderActionResponse : BaseResponse
{
    public OrderActionResponse(Guid correlationId) : base(correlationId) { }
    public OrderActionResponse() { }

    public int OrderId { get; set; }
    public OrderDto? Order { get; set; }
}

// ----- POST /api/orders/{orderId}/refunds -----

public class RefundOrderRequest : BaseRequest
{
    /// <summary>Amount to refund; omit for a full refund of the remaining captured amount.</summary>
    public decimal? Amount { get; set; }

    /// <summary>Caller-supplied idempotency key; repeating it never refunds twice.</summary>
    public string IdempotencyKey { get; set; } = string.Empty;

    [JsonIgnore]
    public int OrderId { get; set; }

    [JsonIgnore]
    public string BuyerId { get; set; } = string.Empty;
}

public class RefundOrderResponse : BaseResponse
{
    public RefundOrderResponse(Guid correlationId) : base(correlationId) { }
    public RefundOrderResponse() { }

    public int RefundId { get; set; }
    public RefundDto? Refund { get; set; }
    public OrderDto? Order { get; set; }
}

// ----- GET /api/my-orders -----

public class MyOrdersResponse : BaseResponse
{
    public MyOrdersResponse(Guid correlationId) : base(correlationId) { }
    public MyOrdersResponse() { }

    public List<OrderDto> Orders { get; set; } = new();
}

// ----- GET /api/reconciliation -----

public class ReconciliationResponse : BaseResponse
{
    public ReconciliationResponse(Guid correlationId) : base(correlationId) { }
    public ReconciliationResponse() { }

    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public int PayPalTransactionCount { get; set; }
    public int MatchedCount { get; set; }
    public int PayPalOnlyCount { get; set; }
    public int EShopOnlyCount { get; set; }
    public List<ReconciliationEntry> Entries { get; set; } = new();
}

// ----- POST /api/payment-methods -----

public class CreatePaymentMethodRequest : BaseRequest
{
    public CardRequestDto Card { get; set; } = new();
    public string? Alias { get; set; }

    [JsonIgnore]
    public string BuyerId { get; set; } = string.Empty;
}

public class CreatePaymentMethodResponse : BaseResponse
{
    public CreatePaymentMethodResponse(Guid correlationId) : base(correlationId) { }
    public CreatePaymentMethodResponse() { }

    public int PaymentMethodId { get; set; }
    public PaymentMethodDto? PaymentMethod { get; set; }
}

// ----- GET /api/payment-methods -----

public class ListPaymentMethodsResponse : BaseResponse
{
    public ListPaymentMethodsResponse(Guid correlationId) : base(correlationId) { }
    public ListPaymentMethodsResponse() { }

    public List<PaymentMethodDto> PaymentMethods { get; set; } = new();
}
