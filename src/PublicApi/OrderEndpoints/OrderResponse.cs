using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class OrderResponse
{
    public int OrderId { get; set; }
    public string BuyerId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public decimal Total { get; set; }
    public string Currency { get; set; } = string.Empty;
    public DateTimeOffset OrderDate { get; set; }
    public AddressDto ShipToAddress { get; set; } = new();
    public List<OrderItemDto> Items { get; set; } = new();
    public PaymentDto? Payment { get; set; }
    public List<RefundDto> Refunds { get; set; } = new();
    public decimal RemainingRefundableAmount { get; set; }

    public static OrderResponse From(Order order, string currency)
    {
        return new OrderResponse
        {
            OrderId = order.Id,
            BuyerId = order.BuyerId,
            Status = order.Status.ToString(),
            Total = order.Total(),
            Currency = order.Payment?.Currency ?? currency,
            OrderDate = order.OrderDate,
            ShipToAddress = new AddressDto
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
            Payment = order.Payment is null ? null : new PaymentDto
            {
                PayPalOrderId = order.Payment.PayPalOrderId,
                PayPalOrderStatus = order.Payment.PayPalOrderStatus,
                AuthorizationId = order.Payment.AuthorizationId,
                AuthorizationStatus = order.Payment.AuthorizationStatus,
                AuthorizationExpirationTime = order.Payment.AuthorizationExpirationTime,
                CaptureId = order.Payment.CaptureId,
                CaptureStatus = order.Payment.CaptureStatus,
                CapturedAmount = order.Payment.CapturedAmount,
                PaypalFee = order.Payment.PaypalFee,
                NetAmount = order.Payment.NetAmount,
                Currency = order.Payment.Currency
            },
            Refunds = order.Refunds.Select(r => new RefundDto
            {
                RefundId = r.Id,
                PayPalRefundId = r.PayPalRefundId,
                Status = r.Status,
                Amount = r.Amount,
                Currency = r.Currency
            }).ToList(),
            RemainingRefundableAmount = order.RemainingRefundableAmount()
        };
    }
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
    public int Units { get; set; }
}

public class PaymentDto
{
    public string PayPalOrderId { get; set; } = string.Empty;
    public string PayPalOrderStatus { get; set; } = string.Empty;
    public string AuthorizationId { get; set; } = string.Empty;
    public string AuthorizationStatus { get; set; } = string.Empty;
    public DateTimeOffset? AuthorizationExpirationTime { get; set; }
    public string? CaptureId { get; set; }
    public string? CaptureStatus { get; set; }
    public decimal? CapturedAmount { get; set; }
    public decimal? PaypalFee { get; set; }
    public decimal? NetAmount { get; set; }
    public string Currency { get; set; } = string.Empty;
}

public class RefundDto
{
    public int RefundId { get; set; }
    public string PayPalRefundId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
}
