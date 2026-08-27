using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class AddressRequest
{
    public string Street { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string? State { get; set; }
    public string Country { get; set; } = string.Empty;
    public string ZipCode { get; set; } = string.Empty;

    public Address ToEntity() => new Address(Street, City, State ?? string.Empty, Country, ZipCode);
}

public class CardDetailsRequest
{
    public string Number { get; set; } = string.Empty;
    public string Expiry { get; set; } = string.Empty;
    public string? SecurityCode { get; set; }
    public string? CardholderName { get; set; }
    public CardBillingAddressRequest? BillingAddress { get; set; }
}

public class CardBillingAddressRequest
{
    public string? AddressLine1 { get; set; }
    public string? AddressLine2 { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? PostalCode { get; set; }
    public string CountryCode { get; set; } = string.Empty;
}

public class OrderItemDto
{
    public int CatalogItemId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public int Units { get; set; }
}

public class RefundDto
{
    public string RefundId { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}

public class PaymentDto
{
    public string Status { get; set; } = string.Empty;
    public string Currency { get; set; } = string.Empty;
    public decimal AuthorizedAmount { get; set; }
    public string? AuthorizationId { get; set; }
    public string? AuthorizationStatus { get; set; }
    public DateTimeOffset? AuthorizationExpiresAt { get; set; }
    public string? CaptureId { get; set; }
    public string? CaptureStatus { get; set; }
    public decimal? CapturedAmount { get; set; }
    public decimal? PayPalFee { get; set; }
    public decimal? NetAmount { get; set; }
    public decimal TotalRefunded { get; set; }
    public decimal RefundableAmount { get; set; }
    public List<RefundDto> Refunds { get; set; } = new();

    public static PaymentDto FromEntity(Payment payment) => new()
    {
        Status = payment.Status.ToString(),
        Currency = payment.Currency,
        AuthorizedAmount = payment.AuthorizedAmount,
        AuthorizationId = payment.AuthorizationId,
        AuthorizationStatus = payment.AuthorizationStatus,
        AuthorizationExpiresAt = payment.AuthorizationExpirationTime,
        CaptureId = payment.CaptureId,
        CaptureStatus = payment.CaptureStatus,
        CapturedAmount = payment.CapturedAmount,
        PayPalFee = payment.PayPalFee,
        NetAmount = payment.NetAmount,
        TotalRefunded = payment.TotalRefunded,
        RefundableAmount = payment.RefundableAmount,
        Refunds = payment.Refunds.Select(r => new RefundDto
        {
            RefundId = r.PayPalRefundId,
            Amount = r.Amount,
            Status = r.Status,
            CreatedAt = r.CreatedAt
        }).ToList()
    };
}

public class OrderDto
{
    public int OrderId { get; set; }
    public DateTimeOffset OrderDate { get; set; }
    public string Status { get; set; } = string.Empty;
    public decimal Total { get; set; }
    public List<OrderItemDto> Items { get; set; } = new();
    public PaymentDto? Payment { get; set; }

    public static OrderDto FromEntity(Order order, Payment? payment) => new()
    {
        OrderId = order.Id,
        OrderDate = order.OrderDate,
        Status = order.Status.ToString(),
        Total = order.Total(),
        Items = order.OrderItems.Select(i => new OrderItemDto
        {
            CatalogItemId = i.ItemOrdered.CatalogItemId,
            ProductName = i.ItemOrdered.ProductName,
            UnitPrice = i.UnitPrice,
            Units = i.Units
        }).ToList(),
        Payment = payment is null ? null : PaymentDto.FromEntity(payment)
    };
}
