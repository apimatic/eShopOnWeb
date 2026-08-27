using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class OrderLineDto
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

public class AddressDto
{
    public string? Street { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? Country { get; set; }
    public string? ZipCode { get; set; }
}

/// <summary>
/// Full card details for a one-off payment. Transient: forwarded to PayPal, never stored.
/// </summary>
public class CardDetailsDto
{
    public string Number { get; set; } = string.Empty;
    /// <summary>Card expiry in YYYY-MM format.</summary>
    public string Expiry { get; set; } = string.Empty;
    public string? SecurityCode { get; set; }
    public string? CardholderName { get; set; }
    public BillingAddressDto? BillingAddress { get; set; }
}

public class BillingAddressDto
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
    public int RefundId { get; set; }
    public string PayPalRefundId { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string IdempotencyKey { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}

public class OrderDto
{
    public int OrderId { get; set; }
    public DateTimeOffset OrderDate { get; set; }
    public string Status { get; set; } = string.Empty;
    public string PaymentStatus { get; set; } = string.Empty;
    public decimal Total { get; set; }
    public string? Currency { get; set; }
    public string? PayPalOrderId { get; set; }
    public string? AuthorizationId { get; set; }
    public DateTimeOffset? AuthorizationExpiresAt { get; set; }
    public string? CaptureId { get; set; }
    public decimal? CapturedAmount { get; set; }
    public decimal? PayPalFee { get; set; }
    public decimal? NetAmount { get; set; }
    public decimal RefundedAmount { get; set; }
    public List<OrderItemDto> Items { get; set; } = new();
    public List<RefundDto> Refunds { get; set; } = new();
}

internal static class OrderMapper
{
    public static OrderDto ToDto(ApplicationCore.Entities.OrderAggregate.Order order)
    {
        return new OrderDto
        {
            OrderId = order.Id,
            OrderDate = order.OrderDate,
            Status = order.Status.ToString(),
            PaymentStatus = order.PaymentStatus.ToString(),
            Total = order.Total(),
            Currency = order.Currency,
            PayPalOrderId = order.PayPalOrderId,
            AuthorizationId = order.PayPalAuthorizationId,
            AuthorizationExpiresAt = order.AuthorizationExpiresAt,
            CaptureId = order.PayPalCaptureId,
            CapturedAmount = order.CapturedAmount,
            PayPalFee = order.PayPalFee,
            NetAmount = order.NetAmount,
            RefundedAmount = order.RefundedAmount,
            Items = MapItems(order),
            Refunds = MapRefunds(order)
        };
    }

    private static List<OrderItemDto> MapItems(ApplicationCore.Entities.OrderAggregate.Order order)
    {
        var items = new List<OrderItemDto>();
        foreach (var item in order.OrderItems)
        {
            items.Add(new OrderItemDto
            {
                CatalogItemId = item.ItemOrdered.CatalogItemId,
                ProductName = item.ItemOrdered.ProductName,
                UnitPrice = item.UnitPrice,
                Units = item.Units
            });
        }
        return items;
    }

    private static List<RefundDto> MapRefunds(ApplicationCore.Entities.OrderAggregate.Order order)
    {
        var refunds = new List<RefundDto>();
        foreach (var refund in order.PaymentRefunds)
        {
            refunds.Add(new RefundDto
            {
                RefundId = refund.Id,
                PayPalRefundId = refund.PayPalRefundId,
                Amount = refund.Amount,
                Currency = refund.Currency,
                Status = refund.Status,
                IdempotencyKey = refund.IdempotencyKey,
                CreatedAt = refund.CreatedAt
            });
        }
        return refunds;
    }

    public static ApplicationCore.Models.Payments.CardDetails ToModel(CardDetailsDto card)
    {
        return new ApplicationCore.Models.Payments.CardDetails(
            card.Number,
            card.Expiry,
            card.SecurityCode,
            card.CardholderName,
            card.BillingAddress is null ? null : new ApplicationCore.Models.Payments.BillingAddress(
                card.BillingAddress.AddressLine1,
                card.BillingAddress.AddressLine2,
                card.BillingAddress.City,
                card.BillingAddress.State,
                card.BillingAddress.PostalCode,
                card.BillingAddress.CountryCode));
    }
}
