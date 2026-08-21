using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.PublicApi;

internal static class ApiCaller
{
    public static string BuyerId(ClaimsPrincipal user)
    {
        var name = user.Identity?.Name;
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new PaymentException(401, "The caller is not authenticated.");
        }

        return name;
    }

    public static OrderDto ToDto(Order order)
    {
        return new OrderDto
        {
            OrderId = order.Id,
            BuyerId = order.BuyerId,
            Status = order.PaymentStatus.ToString(),
            Total = order.Total(),
            Currency = order.Currency,
            OrderDate = order.OrderDate,
            PayPalOrderId = order.PayPalOrderId,
            AuthorizationId = order.AuthorizationId,
            AuthorizationStatus = order.AuthorizationStatus,
            AuthorizationExpiration = order.AuthorizationExpiration,
            CaptureId = order.CaptureId,
            CaptureStatus = order.CaptureStatus,
            CapturedAmount = order.CapturedAmount,
            PaypalFee = order.PaypalFee,
            NetAmount = order.NetAmount,
            GrossAmount = order.GrossAmount,
            RemainingRefundable = order.PaymentStatus is OrderPaymentStatus.Fulfilled
                or OrderPaymentStatus.PartiallyRefunded
                or OrderPaymentStatus.Refunded
                ? order.RemainingRefundable()
                : null,
            Items = order.OrderItems.Select(i => new OrderItemDto
            {
                CatalogItemId = i.ItemOrdered.CatalogItemId,
                ProductName = i.ItemOrdered.ProductName,
                UnitPrice = i.UnitPrice,
                Quantity = i.Units
            }).ToList(),
            Refunds = order.Refunds.Select(r => new OrderRefundDto
            {
                RefundId = r.Id,
                PayPalRefundId = r.PayPalRefundId,
                Status = r.Status,
                Amount = r.Amount
            }).ToList()
        };
    }

    public static PaymentMethodDto ToDto(SavedPaymentMethod method)
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

    public static CardPaymentDetails ToCard(CardDetailsRequest card)
    {
        if (string.IsNullOrWhiteSpace(card.Number)
            || string.IsNullOrWhiteSpace(card.Expiry)
            || string.IsNullOrWhiteSpace(card.SecurityCode))
        {
            throw new PaymentException(400, "Card number, expiry (YYYY-MM), and securityCode are required.");
        }

        BillingAddressDetails? billing = null;
        if (card.BillingAddress != null)
        {
            billing = new BillingAddressDetails(
                card.BillingAddress.CountryCode ?? "US",
                card.BillingAddress.AddressLine1,
                card.BillingAddress.AddressLine2,
                card.BillingAddress.AdminArea2,
                card.BillingAddress.AdminArea1,
                card.BillingAddress.PostalCode);
        }

        return new CardPaymentDetails(
            card.Number.Replace(" ", string.Empty),
            card.Expiry,
            card.SecurityCode,
            card.Name,
            billing);
    }
}

public class OrderDto
{
    public int OrderId { get; set; }
    public string BuyerId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public decimal Total { get; set; }
    public string? Currency { get; set; }
    public DateTimeOffset OrderDate { get; set; }
    public string? PayPalOrderId { get; set; }
    public string? AuthorizationId { get; set; }
    public string? AuthorizationStatus { get; set; }
    public DateTimeOffset? AuthorizationExpiration { get; set; }
    public string? CaptureId { get; set; }
    public string? CaptureStatus { get; set; }
    public decimal? CapturedAmount { get; set; }
    public decimal? PaypalFee { get; set; }
    public decimal? NetAmount { get; set; }
    public decimal? GrossAmount { get; set; }
    public decimal? RemainingRefundable { get; set; }
    public List<OrderItemDto> Items { get; set; } = new();
    public List<OrderRefundDto> Refunds { get; set; } = new();
}

public class OrderItemDto
{
    public int CatalogItemId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public int Quantity { get; set; }
}

public class OrderRefundDto
{
    public int RefundId { get; set; }
    public string PayPalRefundId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public decimal Amount { get; set; }
}

public class PaymentMethodDto
{
    public int PaymentMethodId { get; set; }
    public string? Brand { get; set; }
    public string? LastDigits { get; set; }
    public string? Expiry { get; set; }
    public string? CardholderName { get; set; }
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
    public string? AddressLine1 { get; set; }
    public string? AddressLine2 { get; set; }
    public string? AdminArea2 { get; set; }
    public string? AdminArea1 { get; set; }
    public string? PostalCode { get; set; }
    public string? CountryCode { get; set; }
}
