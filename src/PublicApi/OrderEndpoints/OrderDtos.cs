using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class OrderDto
{
    public int OrderId { get; set; }
    public string BuyerId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public decimal Total { get; set; }
    public string? Currency { get; set; }
    public DateTimeOffset OrderDate { get; set; }
    public List<OrderItemDto> Items { get; set; } = new();
    public PaymentStateDto Payment { get; set; } = new();
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
    public int RefundId { get; set; }
    public string PayPalRefundId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
}

public class CardDetailsRequest
{
    public string Number { get; set; } = string.Empty;
    public string Expiry { get; set; } = string.Empty;
    public string SecurityCode { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public BillingAddressRequest? BillingAddress { get; set; }
}

public class BillingAddressRequest
{
    public string? AddressLine1 { get; set; }
    public string? AddressLine2 { get; set; }
    public string? AdminArea2 { get; set; }
    public string? City { get; set; }
    public string? AdminArea1 { get; set; }
    public string? State { get; set; }
    public string? PostalCode { get; set; }
    public string? CountryCode { get; set; }
}

public class ShipToAddressRequest
{
    public string? Street { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? Country { get; set; }
    public string? ZipCode { get; set; }
}

public static class PaymentApiMapper
{
    public static OrderDto ToDto(Order order)
    {
        return new OrderDto
        {
            OrderId = order.Id,
            BuyerId = order.BuyerId,
            Status = order.Status.ToString(),
            Total = order.Total(),
            Currency = order.PaymentCurrency,
            OrderDate = order.OrderDate,
            Items = order.OrderItems.Select(i => new OrderItemDto
            {
                CatalogItemId = i.ItemOrdered.CatalogItemId,
                ProductName = i.ItemOrdered.ProductName,
                UnitPrice = i.UnitPrice,
                Quantity = i.Units
            }).ToList(),
            Payment = new PaymentStateDto
            {
                PayPalOrderId = order.PayPalOrderId,
                PayPalOrderStatus = order.PayPalOrderStatus,
                AuthorizationId = order.PayPalAuthorizationId,
                AuthorizationStatus = order.PayPalAuthorizationStatus,
                AuthorizationExpiration = order.PayPalAuthorizationExpiration,
                CaptureId = order.PayPalCaptureId,
                CaptureStatus = order.PayPalCaptureStatus,
                CapturedAmount = order.CapturedAmount,
                PaypalFee = order.PaypalFee,
                NetAmount = order.NetAmount,
                Refunds = order.Refunds.Select(r => new RefundDto
                {
                    RefundId = r.Id,
                    PayPalRefundId = r.PayPalRefundId,
                    Status = r.Status,
                    Amount = r.Amount,
                    Currency = r.Currency
                }).ToList()
            }
        };
    }

    public static CardPaymentSource ToCardSource(CardDetailsRequest card)
    {
        var billing = card.BillingAddress;
        return new CardPaymentSource(
            card.Number ?? string.Empty,
            card.Expiry ?? string.Empty,
            card.SecurityCode ?? string.Empty,
            string.IsNullOrWhiteSpace(card.Name) ? "Sandbox Shopper" : card.Name,
            new BillingAddress(
                string.IsNullOrWhiteSpace(billing?.AddressLine1) ? "2211 N First St" : billing!.AddressLine1,
                billing?.AddressLine2,
                FirstNonEmpty(billing?.AdminArea2, billing?.City, "San Jose"),
                FirstNonEmpty(billing?.AdminArea1, billing?.State, "CA"),
                string.IsNullOrWhiteSpace(billing?.PostalCode) ? "95131" : billing!.PostalCode,
                string.IsNullOrWhiteSpace(billing?.CountryCode) ? "US" : billing!.CountryCode));
    }

    public static Address? ToAddress(ShipToAddressRequest? request)
    {
        if (request is null)
        {
            return null;
        }

        return new Address(
            request.Street ?? "N/A",
            request.City ?? "N/A",
            request.State ?? "N/A",
            request.Country ?? "US",
            request.ZipCode ?? "00000");
    }

    private static string FirstNonEmpty(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return string.Empty;
    }
}
