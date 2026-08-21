using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class OrderDto
{
    public int OrderId { get; set; }
    public string BuyerId { get; set; } = string.Empty;
    public string PaymentStatus { get; set; } = string.Empty;
    public decimal Total { get; set; }
    public string? Currency { get; set; }
    public PaymentDto? Payment { get; set; }
    public List<OrderItemDto> Items { get; set; } = new();

    public static OrderDto From(Order order, string? configuredCurrency)
    {
        var dto = new OrderDto
        {
            OrderId = order.Id,
            BuyerId = order.BuyerId,
            PaymentStatus = order.PaymentStatus.ToString(),
            Total = order.Total(),
            Currency = order.Payment?.Currency ?? configuredCurrency
        };

        if (order.Payment is not null)
        {
            dto.Payment = new PaymentDto
            {
                PayPalOrderId = order.Payment.PayPalOrderId,
                PayPalOrderStatus = order.Payment.PayPalOrderStatus,
                AuthorizationId = order.Payment.AuthorizationId,
                AuthorizationStatus = order.Payment.AuthorizationStatus,
                AuthorizationExpiration = order.Payment.AuthorizationExpiration,
                CaptureId = order.Payment.CaptureId,
                CaptureStatus = order.Payment.CaptureStatus,
                CapturedAmount = order.Payment.CapturedAmount,
                PaypalFee = order.Payment.PaypalFee,
                NetAmount = order.Payment.NetAmount,
                Refunds = new List<RefundDto>()
            };

            foreach (var refund in order.Payment.Refunds)
            {
                dto.Payment.Refunds.Add(new RefundDto
                {
                    RefundId = refund.RefundId,
                    Amount = refund.Amount,
                    Status = refund.Status
                });
            }
        }

        foreach (var item in order.OrderItems)
        {
            dto.Items.Add(new OrderItemDto
            {
                CatalogItemId = item.ItemOrdered.CatalogItemId,
                ProductName = item.ItemOrdered.ProductName,
                UnitPrice = item.UnitPrice,
                Units = item.Units
            });
        }

        return dto;
    }
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
    public string? PayPalOrderId { get; set; }
    public string? PayPalOrderStatus { get; set; }
    public string? AuthorizationId { get; set; }
    public string? AuthorizationStatus { get; set; }
    public DateTimeOffset? AuthorizationExpiration { get; set; }
    public string? CaptureId { get; set; }
    public string? CaptureStatus { get; set; }
    public decimal? CapturedAmount { get; set; }
    public decimal? PaypalFee { get; set; }
    public decimal? NetAmount { get; set; }
    public List<RefundDto> Refunds { get; set; } = new();
}

public class RefundDto
{
    public string RefundId { get; set; } = string.Empty;
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

    public CardDetails ToCardDetails()
    {
        BillingAddress? billing = null;
        if (BillingAddress is not null)
        {
            billing = new BillingAddress(
                BillingAddress.CountryCode,
                BillingAddress.AddressLine1,
                BillingAddress.AddressLine2,
                BillingAddress.AdminArea2,
                BillingAddress.AdminArea1,
                BillingAddress.PostalCode);
        }

        return new CardDetails(Number, Expiry, SecurityCode, Name, billing);
    }
}

public class BillingAddressRequest
{
    public string CountryCode { get; set; } = "US";
    public string? AddressLine1 { get; set; }
    public string? AddressLine2 { get; set; }
    public string? AdminArea2 { get; set; }
    public string? AdminArea1 { get; set; }
    public string? PostalCode { get; set; }
}

internal static class Caller
{
    public static string Name(HttpContext httpContext)
    {
        return httpContext.User.Identity?.Name
            ?? throw new PaymentException(401, "The caller is not authenticated.");
    }
}
