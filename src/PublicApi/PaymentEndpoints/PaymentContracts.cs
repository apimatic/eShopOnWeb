using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.PublicApi.Payments;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

public class OrderLineDto
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

public class CardAddressDto
{
    public string Line1 { get; set; } = string.Empty;
    public string? Line2 { get; set; }
    public string City { get; set; } = string.Empty;
    public string Region { get; set; } = string.Empty;
    public string PostalCode { get; set; } = string.Empty;
    public string Country { get; set; } = string.Empty;
}

public class CardDto
{
    public string Number { get; set; } = string.Empty;
    public int ExpirationMonth { get; set; }
    public int ExpirationYear { get; set; }
    public string? SecurityCode { get; set; }
    public string Name { get; set; } = string.Empty;
    public CardAddressDto? Address { get; set; }
}

public class CreateOrderRequest : BaseRequest
{
    public List<OrderLineDto> Items { get; set; } = new();
    public string? Street { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? Country { get; set; }
    public string? ZipCode { get; set; }
}

public class PayOrderRequest : BaseRequest
{
    public int? PaymentMethodId { get; set; }
    public CardDto? Card { get; set; }
}

public class RefundOrderRequest : BaseRequest
{
    /// <summary>Omit to refund the remaining captured amount.</summary>
    public long? AmountCents { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
}

public class CreatePaymentMethodRequest : BaseRequest
{
    public CardDto Card { get; set; } = new();
}

public class CreateOrderResponse : BaseResponse
{
    public CreateOrderResponse(Guid correlationId) : base(correlationId) { }
    public CreateOrderResponse() { }

    public int OrderId { get; set; }
    public decimal Total { get; set; }
    public long TotalCents { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string PaymentStatus { get; set; } = string.Empty;
}

public class PayOrderResponse : BaseResponse
{
    public PayOrderResponse(Guid correlationId) : base(correlationId) { }
    public PayOrderResponse() { }

    public int OrderId { get; set; }
    public string PaymentStatus { get; set; } = string.Empty;
    public string? AuthorizationId { get; set; }
    public string? AuthorizationState { get; set; }
    public long? AuthorizedAmountCents { get; set; }
    public string? Currency { get; set; }
    public int? PaymentMethodId { get; set; }
}

public class FulfilOrderResponse : BaseResponse
{
    public FulfilOrderResponse(Guid correlationId) : base(correlationId) { }
    public FulfilOrderResponse() { }

    public int OrderId { get; set; }
    public string PaymentStatus { get; set; } = string.Empty;
    public string? CaptureTransferId { get; set; }
    public string? CaptureState { get; set; }
    public long? CapturedAmountCents { get; set; }
    public long? FeeCents { get; set; }
    public long? NetCents { get; set; }
    public DateTimeOffset? FulfilledAt { get; set; }
}

public class CancelOrderResponse : BaseResponse
{
    public CancelOrderResponse(Guid correlationId) : base(correlationId) { }
    public CancelOrderResponse() { }

    public int OrderId { get; set; }
    public string PaymentStatus { get; set; } = string.Empty;
    public string? AuthorizationId { get; set; }
    public string? AuthorizationState { get; set; }
    public bool IsVoid { get; set; }
    public DateTimeOffset? CancelledAt { get; set; }
}

public class RefundOrderResponse : BaseResponse
{
    public RefundOrderResponse(Guid correlationId) : base(correlationId) { }
    public RefundOrderResponse() { }

    public string RefundId { get; set; } = string.Empty;
    public int OrderId { get; set; }
    public long AmountCents { get; set; }
    public long TotalRefundedCents { get; set; }
    public long RemainingRefundableCents { get; set; }
    public string? State { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public class PaymentMethodDto
{
    public int PaymentMethodId { get; set; }
    public string? Brand { get; set; }
    public string? LastFour { get; set; }
    public int? ExpirationMonth { get; set; }
    public int? ExpirationYear { get; set; }
    public string? CardholderName { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public class CreatePaymentMethodResponse : BaseResponse
{
    public CreatePaymentMethodResponse(Guid correlationId) : base(correlationId) { }
    public CreatePaymentMethodResponse() { }

    public int PaymentMethodId { get; set; }
    public string? Brand { get; set; }
    public string? LastFour { get; set; }
    public int? ExpirationMonth { get; set; }
    public int? ExpirationYear { get; set; }
    public string? CardholderName { get; set; }
}

public class ListPaymentMethodsResponse : BaseResponse
{
    public ListPaymentMethodsResponse(Guid correlationId) : base(correlationId) { }
    public ListPaymentMethodsResponse() { }

    public List<PaymentMethodDto> PaymentMethods { get; set; } = new();
}

public class DeletePaymentMethodResponse : BaseResponse
{
    public DeletePaymentMethodResponse(Guid correlationId) : base(correlationId) { }
    public DeletePaymentMethodResponse() { }

    public int PaymentMethodId { get; set; }
    public bool Deleted { get; set; }
}

public class OrderItemSummaryDto
{
    public int CatalogItemId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public int Units { get; set; }
}

public class OrderSummaryDto
{
    public int OrderId { get; set; }
    public DateTimeOffset OrderDate { get; set; }
    public decimal Total { get; set; }
    public long TotalCents { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string PaymentStatus { get; set; } = string.Empty;
    public string? AuthorizationId { get; set; }
    public string? AuthorizationState { get; set; }
    public long? AuthorizedAmountCents { get; set; }
    public string? CaptureTransferId { get; set; }
    public string? CaptureState { get; set; }
    public long? CapturedAmountCents { get; set; }
    public long? FeeCents { get; set; }
    public long? NetCents { get; set; }
    public long RefundedAmountCents { get; set; }
    public long RemainingRefundableCents { get; set; }
    public DateTimeOffset? FulfilledAt { get; set; }
    public DateTimeOffset? CancelledAt { get; set; }
    public List<OrderItemSummaryDto> Items { get; set; } = new();
}

public class MyOrdersResponse : BaseResponse
{
    public MyOrdersResponse(Guid correlationId) : base(correlationId) { }
    public MyOrdersResponse() { }

    public List<OrderSummaryDto> Orders { get; set; } = new();
}

public class ReconciliationResponse : BaseResponse
{
    public ReconciliationResponse(Guid correlationId) : base(correlationId) { }
    public ReconciliationResponse() { }

    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public int PagesFetched { get; set; }
    public bool Truncated { get; set; }
    public List<ReconciliationTransaction> Transactions { get; set; } = new();
    public List<ReconciliationTransaction> UnmatchedFinix { get; set; } = new();
    public List<UnmatchedEshopTransaction> UnmatchedEshop { get; set; } = new();
}

public static class PaymentMappings
{
    public static CardInput ToInput(this CardDto dto) => new(
        dto.Number,
        dto.ExpirationMonth,
        dto.ExpirationYear,
        dto.SecurityCode,
        dto.Name,
        dto.Address is null
            ? null
            : new CardAddressInput(
                dto.Address.Line1,
                dto.Address.Line2,
                dto.Address.City,
                dto.Address.Region,
                dto.Address.PostalCode,
                dto.Address.Country));

    public static PaymentMethodDto ToDto(this SavedPaymentMethod method) => new()
    {
        PaymentMethodId = method.Id,
        Brand = method.Brand,
        LastFour = method.LastFour,
        ExpirationMonth = method.ExpirationMonth,
        ExpirationYear = method.ExpirationYear,
        CardholderName = method.CardholderName,
        CreatedAt = method.CreatedAt
    };

    public static OrderSummaryDto ToSummary(this Order order) => new()
    {
        OrderId = order.Id,
        OrderDate = order.OrderDate,
        Total = order.Total(),
        TotalCents = (long)Math.Round(order.Total() * 100m, MidpointRounding.AwayFromZero),
        Currency = order.Currency ?? string.Empty,
        PaymentStatus = order.PaymentStatus.ToString(),
        AuthorizationId = order.FinixAuthorizationId,
        AuthorizationState = order.AuthorizationState,
        AuthorizedAmountCents = order.AuthorizedAmountCents,
        CaptureTransferId = order.CaptureTransferId,
        CaptureState = order.CaptureState,
        CapturedAmountCents = order.CapturedAmountCents,
        FeeCents = order.FinixFeeCents,
        NetCents = order.NetProceedsCents,
        RefundedAmountCents = order.RefundedAmountCents,
        RemainingRefundableCents = order.RemainingRefundableCents,
        FulfilledAt = order.FulfilledAt,
        CancelledAt = order.CancelledAt,
        Items = order.OrderItems.Select(i => new OrderItemSummaryDto
        {
            CatalogItemId = i.ItemOrdered.CatalogItemId,
            ProductName = i.ItemOrdered.ProductName,
            UnitPrice = i.UnitPrice,
            Units = i.Units
        }).ToList()
    };
}
