using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class OrderDto
{
    public int OrderId { get; set; }
    public string BuyerId { get; set; } = string.Empty;
    public DateTimeOffset OrderDate { get; set; }
    public string Status { get; set; } = string.Empty;
    public decimal Total { get; set; }
    public string Currency { get; set; } = string.Empty;
    public AddressDto? ShipTo { get; set; }
    public List<OrderItemDto> Items { get; set; } = new();
    public PaymentDto Payment { get; set; } = new();
    public List<RefundDto> Refunds { get; set; } = new();

    public static OrderDto From(Order order, string fallbackCurrency)
    {
        return new OrderDto
        {
            OrderId = order.Id,
            BuyerId = order.BuyerId,
            OrderDate = order.OrderDate,
            Status = order.Status.ToString(),
            Total = order.Total(),
            Currency = string.IsNullOrWhiteSpace(order.Payment.Currency) ? fallbackCurrency : order.Payment.Currency,
            ShipTo = order.ShipToAddress is null
                ? null
                : new AddressDto
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
                Units = i.Units
            }).ToList(),
            Payment = new PaymentDto
            {
                PayPalOrderId = order.Payment.PayPalOrderId,
                PayPalOrderStatus = order.Payment.PayPalOrderStatus,
                AuthorizationId = order.Payment.AuthorizationId,
                AuthorizationStatus = order.Payment.AuthorizationStatus,
                AuthorizationCreatedAt = order.Payment.AuthorizationCreatedAt,
                AuthorizationExpiresAt = order.Payment.AuthorizationExpiresAt,
                CaptureId = order.Payment.CaptureId,
                CaptureStatus = order.Payment.CaptureStatus,
                CapturedAmount = order.Payment.CapturedAmount,
                PayPalFee = order.Payment.PayPalFee,
                NetAmount = order.Payment.NetAmount,
                CapturedAt = order.Payment.CapturedAt,
                RefundedAmount = order.Payment.RefundedAmount,
                RemainingRefundableAmount = order.Payment.RemainingRefundableAmount
            },
            Refunds = order.Refunds.Select(r => new RefundDto
            {
                RefundId = r.Id,
                PayPalRefundId = r.PayPalRefundId,
                Status = r.PayPalRefundStatus,
                Amount = r.Amount,
                Currency = r.Currency,
                IdempotencyKey = r.IdempotencyKey,
                CreatedAt = r.CreatedAt
            }).ToList()
        };
    }
}

public class OrderItemDto
{
    public int CatalogItemId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public int Units { get; set; }
}

public class AddressDto
{
    public string Street { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string Country { get; set; } = string.Empty;
    public string ZipCode { get; set; } = string.Empty;
}

public class PaymentDto
{
    public string? PayPalOrderId { get; set; }
    public string? PayPalOrderStatus { get; set; }
    public string? AuthorizationId { get; set; }
    public string? AuthorizationStatus { get; set; }
    public DateTimeOffset? AuthorizationCreatedAt { get; set; }
    public DateTimeOffset? AuthorizationExpiresAt { get; set; }
    public string? CaptureId { get; set; }
    public string? CaptureStatus { get; set; }
    public decimal? CapturedAmount { get; set; }
    public decimal? PayPalFee { get; set; }
    public decimal? NetAmount { get; set; }
    public DateTimeOffset? CapturedAt { get; set; }
    public decimal RefundedAmount { get; set; }
    public decimal RemainingRefundableAmount { get; set; }
}

public class RefundDto
{
    public int RefundId { get; set; }
    public string PayPalRefundId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string IdempotencyKey { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}

public class PaymentMethodDto
{
    public int PaymentMethodId { get; set; }
    public string Brand { get; set; } = string.Empty;
    public string LastDigits { get; set; } = string.Empty;
    public string Expiry { get; set; } = string.Empty;
    public string? CardholderName { get; set; }

    public static PaymentMethodDto From(ShopperPaymentMethod method)
    {
        return new PaymentMethodDto
        {
            PaymentMethodId = method.Id,
            Brand = method.Brand,
            LastDigits = method.LastDigits,
            Expiry = method.Expiry,
            CardholderName = method.CardholderName
        };
    }
}
