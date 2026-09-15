using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class OrderResponse
{
    public int OrderId { get; set; }
    public string BuyerId { get; set; } = string.Empty;
    public DateTimeOffset OrderDate { get; set; }
    public string PaymentStatus { get; set; } = string.Empty;
    public decimal Total { get; set; }
    public string? Currency { get; set; }
    public AddressDto? ShipTo { get; set; }
    public List<OrderItemDto> Items { get; set; } = new();
    public PaymentStateDto? Payment { get; set; }
    public List<RefundDto> Refunds { get; set; } = new();
}

public class AddressDto
{
    public string Street { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string Country { get; set; } = string.Empty;
    public string ZipCode { get; set; } = string.Empty;
}

public class OrderItemDto
{
    public int CatalogItemId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public int Quantity { get; set; }
}

public class PaymentStateDto
{
    public string? PayPalOrderId { get; set; }
    public string? PayPalOrderStatus { get; set; }
    public string? AuthorizationId { get; set; }
    public string? AuthorizationStatus { get; set; }
    public DateTimeOffset? AuthorizationCreatedAt { get; set; }
    public DateTimeOffset? AuthorizationExpiration { get; set; }
    public string? CaptureId { get; set; }
    public string? CaptureStatus { get; set; }
    public decimal? CapturedAmount { get; set; }
    public decimal? PaypalFee { get; set; }
    public decimal? NetProceeds { get; set; }
}

public class RefundDto
{
    public int RefundId { get; set; }
    public string? PayPalRefundId { get; set; }
    public string Status { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}

public static class OrderResponseMapper
{
    public static OrderResponse Map(Order order)
    {
        return new OrderResponse
        {
            OrderId = order.Id,
            BuyerId = order.BuyerId,
            OrderDate = order.OrderDate,
            PaymentStatus = order.PaymentStatus.ToString(),
            Total = order.Total(),
            Currency = order.Payment.Currency,
            ShipTo = new AddressDto
            {
                Street = order.ShipToAddress.Street,
                City = order.ShipToAddress.City,
                State = order.ShipToAddress.State,
                Country = order.ShipToAddress.Country,
                ZipCode = order.ShipToAddress.ZipCode
            },
            Items = order.OrderItems.Select(i => new OrderItemDto
            {
                CatalogItemId = i.ItemOrdered.CatalogItemId,
                ProductName = i.ItemOrdered.ProductName,
                UnitPrice = i.UnitPrice,
                Quantity = i.Units
            }).ToList(),
            Payment = new PaymentStateDto
            {
                PayPalOrderId = order.Payment.PayPalOrderId,
                PayPalOrderStatus = order.Payment.PayPalOrderStatus,
                AuthorizationId = order.Payment.AuthorizationId,
                AuthorizationStatus = order.Payment.AuthorizationStatus,
                AuthorizationCreatedAt = order.Payment.AuthorizationCreatedAt,
                AuthorizationExpiration = order.Payment.AuthorizationExpiration,
                CaptureId = order.Payment.CaptureId,
                CaptureStatus = order.Payment.CaptureStatus,
                CapturedAmount = order.Payment.CapturedAmount,
                PaypalFee = order.Payment.PaypalFee,
                NetProceeds = order.Payment.NetProceeds
            },
            Refunds = order.Refunds.Select(r => new RefundDto
            {
                RefundId = r.Id,
                PayPalRefundId = r.PayPalRefundId,
                Status = r.Status,
                Amount = r.Amount,
                Currency = r.Currency,
                CreatedAt = r.CreatedAt
            }).ToList()
        };
    }
}
