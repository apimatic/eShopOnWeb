using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class OrderItemLineRequest
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

public class AddressRequest
{
    public string? Street { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? Country { get; set; }
    public string? ZipCode { get; set; }
}

public class CreateOrderRequest : BaseRequest
{
    [JsonIgnore]
    public string? BuyerId { get; set; }
    public List<OrderItemLineRequest> Items { get; set; } = new();
    public AddressRequest? ShipTo { get; set; }
}

public class CardRequest
{
    public string Number { get; set; } = string.Empty;
    public string Expiry { get; set; } = string.Empty;
    public string? SecurityCode { get; set; }
    public string? Name { get; set; }
    public CardBillingAddressRequest? BillingAddress { get; set; }
}

public class CardBillingAddressRequest
{
    public string? AddressLine1 { get; set; }
    public string? AddressLine2 { get; set; }
    public string? AdminArea1 { get; set; }
    public string? AdminArea2 { get; set; }
    public string? PostalCode { get; set; }
    public string? CountryCode { get; set; }
}

public class PayOrderRequest : BaseRequest
{
    [JsonIgnore]
    public string? BuyerId { get; set; }
    [JsonIgnore]
    public int OrderId { get; set; }
    public CardRequest? Card { get; set; }
    public int? PaymentMethodId { get; set; }
}

public class RefundOrderRequest : BaseRequest
{
    [JsonIgnore]
    public string? BuyerId { get; set; }
    [JsonIgnore]
    public int OrderId { get; set; }
    public decimal? Amount { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
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
    public string Currency { get; set; } = string.Empty;
    public string IdempotencyKey { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}

public class PaymentDto
{
    public string? PayPalOrderId { get; set; }
    public string? InvoiceId { get; set; }
    public string? AuthorizationId { get; set; }
    public string? AuthorizationStatus { get; set; }
    public DateTimeOffset? AuthorizationCreateTime { get; set; }
    public DateTimeOffset? AuthorizationExpirationTime { get; set; }
    public string? CaptureId { get; set; }
    public string? CaptureStatus { get; set; }
    public decimal? CapturedAmount { get; set; }
    public decimal? PaypalFee { get; set; }
    public decimal? NetAmount { get; set; }
    public string? Currency { get; set; }
    public decimal RefundedAmount { get; set; }
    public decimal RefundableRemaining { get; set; }
    public List<RefundDto> Refunds { get; set; } = new();
}

public class OrderDto
{
    public int OrderId { get; set; }
    public string BuyerId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public decimal Total { get; set; }
    public string Currency { get; set; } = string.Empty;
    public DateTimeOffset OrderDate { get; set; }
    public List<OrderItemDto> Items { get; set; } = new();
    public PaymentDto? Payment { get; set; }
}

public class CreateOrderResponse : BaseResponse
{
    public CreateOrderResponse(Guid correlationId) : base(correlationId) { }
    public int OrderId { get; set; }
    public OrderDto Order { get; set; } = new();
}

public class OrderActionResponse : BaseResponse
{
    public OrderActionResponse(Guid correlationId) : base(correlationId) { }
    public int OrderId { get; set; }
    public OrderDto Order { get; set; } = new();
}

public class RefundOrderResponse : BaseResponse
{
    public RefundOrderResponse(Guid correlationId) : base(correlationId) { }
    public int OrderId { get; set; }
    public string RefundId { get; set; } = string.Empty;
    public OrderDto Order { get; set; } = new();
}

public class ListOrdersResponse : BaseResponse
{
    public List<OrderDto> Orders { get; set; } = new();
}

public static class OrderDtoMapper
{
    public static OrderDto ToDto(Order order, string currency)
    {
        return new OrderDto
        {
            OrderId = order.Id,
            BuyerId = order.BuyerId,
            Status = order.Status.ToString(),
            Total = order.Total(),
            Currency = order.Payment?.Currency ?? currency,
            OrderDate = order.OrderDate,
            Items = order.OrderItems.Select(i => new OrderItemDto
            {
                CatalogItemId = i.ItemOrdered.CatalogItemId,
                ProductName = i.ItemOrdered.ProductName,
                UnitPrice = i.UnitPrice,
                Quantity = i.Units
            }).ToList(),
            Payment = order.Payment is null ? null : ToPaymentDto(order.Payment)
        };
    }

    public static PaymentDto ToPaymentDto(OrderPayment payment) => new()
    {
        PayPalOrderId = payment.PayPalOrderId,
        InvoiceId = payment.InvoiceId,
        AuthorizationId = payment.AuthorizationId,
        AuthorizationStatus = payment.AuthorizationStatus,
        AuthorizationCreateTime = payment.AuthorizationCreateTime,
        AuthorizationExpirationTime = payment.AuthorizationExpirationTime,
        CaptureId = payment.CaptureId,
        CaptureStatus = payment.CaptureStatus,
        CapturedAmount = payment.CapturedAmount,
        PaypalFee = payment.PaypalFee,
        NetAmount = payment.NetAmount,
        Currency = payment.Currency,
        RefundedAmount = payment.TotalRefunded,
        RefundableRemaining = payment.RefundableRemaining,
        Refunds = payment.Refunds.Select(r => new RefundDto
        {
            RefundId = r.PayPalRefundId,
            Status = r.Status,
            Amount = r.Amount,
            Currency = r.Currency,
            IdempotencyKey = r.IdempotencyKey,
            CreatedAt = r.CreatedAt
        }).ToList()
    };

    public static CardPaymentDetails ToCardDetails(CardRequest card) => new(
        card.Number,
        card.Expiry,
        card.SecurityCode,
        card.Name,
        card.BillingAddress is null
            ? null
            : new CardBillingAddress(
                card.BillingAddress.AddressLine1,
                card.BillingAddress.AddressLine2,
                card.BillingAddress.AdminArea2,
                card.BillingAddress.AdminArea1,
                card.BillingAddress.PostalCode,
                card.BillingAddress.CountryCode));

    public static Address? ToAddress(AddressRequest? shipTo)
    {
        if (shipTo is null)
        {
            return null;
        }

        return new Address(
            shipTo.Street ?? string.Empty,
            shipTo.City ?? string.Empty,
            shipTo.State ?? string.Empty,
            shipTo.Country ?? string.Empty,
            shipTo.ZipCode ?? string.Empty);
    }
}

public class PaymentMethodDto
{
    public int PaymentMethodId { get; set; }
    public string Brand { get; set; } = string.Empty;
    public string LastDigits { get; set; } = string.Empty;
    public string? Expiry { get; set; }
    public string? CardholderName { get; set; }

    public static PaymentMethodDto From(SavedPaymentMethod method) => new()
    {
        PaymentMethodId = method.Id,
        Brand = method.Brand ?? "CARD",
        LastDigits = method.LastDigits,
        Expiry = method.Expiry,
        CardholderName = method.CardholderName
    };
}
