using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class OrderDto
{
    public int OrderId { get; set; }
    public string BuyerId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public decimal Total { get; set; }
    public string? Currency { get; set; }
    public DateTimeOffset OrderDate { get; set; }
    public AddressDto? ShipToAddress { get; set; }
    public List<OrderItemDto> Items { get; set; } = new();
    public PaymentStateDto? Payment { get; set; }
    public List<RefundDto> Refunds { get; set; } = new();

    public static OrderDto From(Order order) => new()
    {
        OrderId = order.Id,
        BuyerId = order.BuyerId,
        Status = order.Status.ToString(),
        Total = order.Total(),
        Currency = order.Currency,
        OrderDate = order.OrderDate,
        ShipToAddress = order.ShipToAddress is null ? null : new AddressDto
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
        Payment = new PaymentStateDto
        {
            PayPalOrderId = order.PayPalOrderId,
            AuthorizationId = order.PayPalAuthorizationId,
            AuthorizationStatus = order.PayPalAuthorizationStatus,
            AuthorizationCreated = order.PayPalAuthorizationCreated,
            AuthorizationExpiration = order.PayPalAuthorizationExpiration,
            CaptureId = order.PayPalCaptureId,
            CaptureStatus = order.PayPalCaptureStatus,
            CapturedAmount = order.CapturedAmount,
            PayPalFee = order.PayPalFee,
            NetAmount = order.NetAmount,
            RefundedAmount = order.RefundedTotal(),
            RefundableRemaining = order.RefundableRemaining()
        },
        Refunds = order.Refunds.Select(r => new RefundDto
        {
            RefundId = r.Id,
            PayPalRefundId = r.PayPalRefundId,
            Status = r.PayPalRefundStatus,
            Amount = r.Amount,
            IdempotencyKey = r.IdempotencyKey,
            CreatedAt = r.CreatedAt
        }).ToList()
    };
}

public class OrderItemDto
{
    public int CatalogItemId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public int Units { get; set; }
}

public class PaymentStateDto
{
    public string? PayPalOrderId { get; set; }
    public string? AuthorizationId { get; set; }
    public string? AuthorizationStatus { get; set; }
    public DateTimeOffset? AuthorizationCreated { get; set; }
    public DateTimeOffset? AuthorizationExpiration { get; set; }
    public string? CaptureId { get; set; }
    public string? CaptureStatus { get; set; }
    public decimal? CapturedAmount { get; set; }
    public decimal? PayPalFee { get; set; }
    public decimal? NetAmount { get; set; }
    public decimal RefundedAmount { get; set; }
    public decimal RefundableRemaining { get; set; }
}

public class RefundDto
{
    public int RefundId { get; set; }
    public string PayPalRefundId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}

public class AddressDto
{
    public string? Street { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? Country { get; set; }
    public string? ZipCode { get; set; }
    public string? AddressLine1 { get; set; }
    public string? AddressLine2 { get; set; }
    public string? AdminArea2 { get; set; }
    public string? AdminArea1 { get; set; }
    public string? PostalCode { get; set; }
    public string? CountryCode { get; set; }
}

public class CardRequest
{
    public string? Number { get; set; }
    public string? Expiry { get; set; }
    public string? SecurityCode { get; set; }
    public string? Name { get; set; }
    public AddressDto? BillingAddress { get; set; }
}
