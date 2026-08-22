using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class OrderDto
{
    public int OrderId { get; set; }
    public string BuyerId { get; set; } = string.Empty;
    public string PaymentStatus { get; set; } = string.Empty;
    public decimal Total { get; set; }
    public string? Currency { get; set; }
    public DateTimeOffset OrderDate { get; set; }
    public string? PayPalOrderId { get; set; }
    public string? PayPalAuthorizationId { get; set; }
    public string? AuthorizationStatus { get; set; }
    public DateTimeOffset? AuthorizationExpiresAt { get; set; }
    public string? PayPalCaptureId { get; set; }
    public string? CaptureStatus { get; set; }
    public decimal? CapturedAmount { get; set; }
    public decimal? PaypalFee { get; set; }
    public decimal? NetAmount { get; set; }
    public decimal RefundedAmount { get; set; }
    public decimal RemainingRefundable { get; set; }
    public List<OrderItemDto> Items { get; set; } = new();
    public List<RefundDto> Refunds { get; set; } = new();

    public static OrderDto From(Order order)
    {
        return new OrderDto
        {
            OrderId = order.Id,
            BuyerId = order.BuyerId,
            PaymentStatus = order.PaymentStatus.ToString(),
            Total = order.Total(),
            Currency = order.Currency,
            OrderDate = order.OrderDate,
            PayPalOrderId = order.PayPalOrderId,
            PayPalAuthorizationId = order.PayPalAuthorizationId,
            AuthorizationStatus = order.AuthorizationStatus,
            AuthorizationExpiresAt = order.AuthorizationExpiresAt,
            PayPalCaptureId = order.PayPalCaptureId,
            CaptureStatus = order.CaptureStatus,
            CapturedAmount = order.CapturedAmount,
            PaypalFee = order.PaypalFee,
            NetAmount = order.NetAmount,
            RefundedAmount = order.RefundedTotal(),
            RemainingRefundable = order.RemainingRefundable(),
            Items = order.OrderItems.Select(i => new OrderItemDto
            {
                CatalogItemId = i.ItemOrdered.CatalogItemId,
                ProductName = i.ItemOrdered.ProductName,
                UnitPrice = i.UnitPrice,
                Units = i.Units
            }).ToList(),
            Refunds = order.Refunds.Select(r => new RefundDto
            {
                RefundId = r.Id,
                PayPalRefundId = r.PayPalRefundId,
                Amount = r.Amount,
                Status = r.Status
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

public class RefundDto
{
    public int RefundId { get; set; }
    public string PayPalRefundId { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Status { get; set; } = string.Empty;
}

public class CardDetailsRequest
{
    public string Number { get; set; } = string.Empty;
    public string Expiry { get; set; } = string.Empty;
    public string SecurityCode { get; set; } = string.Empty;
    public string? Name { get; set; }
    public BillingAddressRequest? BillingAddress { get; set; }
}

public class BillingAddressRequest
{
    public string? Line1 { get; set; }
    public string? Line2 { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? PostalCode { get; set; }
    public string CountryCode { get; set; } = "US";
}

public static class CardMapping
{
    public static CardPaymentDetails ToDetails(CardDetailsRequest card)
    {
        BillingAddressDetails? address = null;
        if (card.BillingAddress != null)
        {
            address = new BillingAddressDetails(
                card.BillingAddress.Line1,
                card.BillingAddress.Line2,
                card.BillingAddress.City,
                card.BillingAddress.State,
                card.BillingAddress.PostalCode,
                string.IsNullOrWhiteSpace(card.BillingAddress.CountryCode) ? "US" : card.BillingAddress.CountryCode);
        }

        return new CardPaymentDetails(
            card.Number,
            card.Expiry,
            card.SecurityCode,
            card.Name,
            address);
    }
}
