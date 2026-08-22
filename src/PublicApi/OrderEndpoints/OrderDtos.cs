using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class CreateOrderRequest : BaseRequest
{
    public List<CreateOrderLineRequest> Items { get; set; } = new();
    public ShippingAddressRequest? ShipTo { get; set; }

    [JsonIgnore]
    public string BuyerId { get; set; } = string.Empty;
}

public class CreateOrderLineRequest
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

public class ShippingAddressRequest
{
    public string Street { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string Country { get; set; } = string.Empty;
    public string ZipCode { get; set; } = string.Empty;
}

public class CreateOrderResponse : BaseResponse
{
    public CreateOrderResponse(Guid correlationId) : base(correlationId) { }
    public CreateOrderResponse() { }
    public int OrderId { get; set; }
    public OrderDto Order { get; set; } = new();
}

public class PayOrderRequest : BaseRequest
{
    public CardRequestDto? Card { get; set; }
    public string? PaymentMethodId { get; set; }

    [JsonIgnore]
    public int OrderId { get; set; }

    [JsonIgnore]
    public string BuyerId { get; set; } = string.Empty;
}

public class CardRequestDto
{
    public string Name { get; set; } = string.Empty;
    public string Number { get; set; } = string.Empty;
    public string Expiry { get; set; } = string.Empty;
    public string SecurityCode { get; set; } = string.Empty;
    public BillingAddressDto? BillingAddress { get; set; }
}

public class BillingAddressDto
{
    public string? CountryCode { get; set; }
    public string? AddressLine1 { get; set; }
    public string? AddressLine2 { get; set; }
    public string? AdminArea2 { get; set; }
    public string? AdminArea1 { get; set; }
    public string? PostalCode { get; set; }
}

public class RefundOrderRequest : BaseRequest
{
    public decimal? Amount { get; set; }
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
    public string RefundId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public decimal Amount { get; set; }
}

public class OrderDto
{
    public int OrderId { get; set; }
    public string BuyerId { get; set; } = string.Empty;
    public DateTimeOffset OrderDate { get; set; }
    public string PaymentStatus { get; set; } = string.Empty;
    public decimal Total { get; set; }
    public string? PayPalOrderId { get; set; }
    public string? AuthorizationId { get; set; }
    public string? AuthorizationStatus { get; set; }
    public DateTimeOffset? AuthorizationExpiresAt { get; set; }
    public string? CaptureId { get; set; }
    public string? CaptureStatus { get; set; }
    public decimal? CapturedAmount { get; set; }
    public decimal? PaypalFee { get; set; }
    public decimal? NetAmount { get; set; }
    public decimal RemainingRefundable { get; set; }
    public List<OrderItemDto> Items { get; set; } = new();
    public List<RefundDto> Refunds { get; set; } = new();
}

public class OrderItemDto
{
    public int CatalogItemId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public int Quantity { get; set; }
}

public class RefundDto
{
    public string RefundId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public decimal Amount { get; set; }
}

public class ListOrdersResponse : BaseResponse
{
    public List<OrderDto> Orders { get; set; } = new();
}

public static class OrderDtoMapper
{
    public static OrderDto ToDto(Order order) => new()
    {
        OrderId = order.Id,
        BuyerId = order.BuyerId,
        OrderDate = order.OrderDate,
        PaymentStatus = order.PaymentStatus.ToString(),
        Total = order.Total(),
        PayPalOrderId = order.PayPalOrderId,
        AuthorizationId = order.PayPalAuthorizationId,
        AuthorizationStatus = order.PayPalAuthorizationStatus,
        AuthorizationExpiresAt = order.AuthorizationExpiresAt,
        CaptureId = order.PayPalCaptureId,
        CaptureStatus = order.PayPalCaptureStatus,
        CapturedAmount = order.CapturedAmount,
        PaypalFee = order.PaypalFee,
        NetAmount = order.NetAmount,
        RemainingRefundable = order.RemainingRefundable(),
        Items = order.OrderItems.Select(i => new OrderItemDto
        {
            CatalogItemId = i.ItemOrdered.CatalogItemId,
            ProductName = i.ItemOrdered.ProductName,
            UnitPrice = i.UnitPrice,
            Quantity = i.Units
        }).ToList(),
        Refunds = order.Refunds.Select(r => new RefundDto
        {
            RefundId = r.PayPalRefundId,
            Status = r.Status,
            Amount = r.Amount
        }).ToList()
    };

    public static CardPaymentSource ToCardSource(CardRequestDto card) =>
        new(
            card.Name,
            card.Number,
            card.Expiry,
            card.SecurityCode,
            card.BillingAddress is null
                ? null
                : new BillingAddressInfo(
                    card.BillingAddress.CountryCode ?? "US",
                    card.BillingAddress.AddressLine1,
                    card.BillingAddress.AddressLine2,
                    card.BillingAddress.AdminArea2,
                    card.BillingAddress.AdminArea1,
                    card.BillingAddress.PostalCode));
}
