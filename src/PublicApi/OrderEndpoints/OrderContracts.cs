using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class OrderLineRequest
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

public class ShippingAddressRequest
{
    public string Street { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string Country { get; set; } = string.Empty;
    public string ZipCode { get; set; } = string.Empty;
}

public class CardRequestDto
{
    public string Number { get; set; } = string.Empty;
    public string Expiry { get; set; } = string.Empty;
    public string? SecurityCode { get; set; }
    public string? Name { get; set; }
    public CardBillingAddressDto? BillingAddress { get; set; }

    public override string ToString() => "CardRequestDto(redacted)";
}

public class CardBillingAddressDto
{
    public string? AddressLine1 { get; set; }
    public string? AddressLine2 { get; set; }
    public string? AdminArea1 { get; set; }
    public string? AdminArea2 { get; set; }
    public string? PostalCode { get; set; }
    public string CountryCode { get; set; } = "US";
}

public class OrderItemResponse
{
    public int CatalogItemId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public int Units { get; set; }
}

public class RefundResponse
{
    public int RefundId { get; set; }
    public string PayPalRefundId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public decimal Amount { get; set; }
}

public class PaymentStateResponse
{
    public string Status { get; set; } = string.Empty;
    public string? Currency { get; set; }
    public decimal Total { get; set; }
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
    public decimal RemainingRefundable { get; set; }
    public List<RefundResponse> Refunds { get; set; } = new();
}

public class OrderResponse
{
    public int OrderId { get; set; }
    public string BuyerId { get; set; } = string.Empty;
    public DateTimeOffset OrderDate { get; set; }
    public PaymentStateResponse Payment { get; set; } = new();
    public List<OrderItemResponse> Items { get; set; } = new();
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
            Payment = new PaymentStateResponse
            {
                Status = order.PaymentStatus.ToString(),
                Currency = order.Currency,
                Total = order.Total(),
                PayPalOrderId = order.PayPalOrderId,
                PayPalOrderStatus = order.PayPalOrderStatus,
                AuthorizationId = order.AuthorizationId,
                AuthorizationStatus = order.AuthorizationStatus,
                AuthorizationExpiration = order.AuthorizationExpiration,
                CaptureId = order.CaptureId,
                CaptureStatus = order.CaptureStatus,
                CapturedAmount = order.CapturedAmount,
                PaypalFee = order.PaypalFee,
                NetAmount = order.NetAmount,
                RemainingRefundable = order.RemainingRefundable(),
                Refunds = order.Refunds.Select(MapRefund).ToList()
            },
            Items = order.OrderItems.Select(i => new OrderItemResponse
            {
                CatalogItemId = i.ItemOrdered.CatalogItemId,
                ProductName = i.ItemOrdered.ProductName,
                UnitPrice = i.UnitPrice,
                Units = i.Units
            }).ToList()
        };
    }

    public static RefundResponse MapRefund(OrderRefund refund) =>
        new()
        {
            RefundId = refund.Id,
            PayPalRefundId = refund.PayPalRefundId,
            Status = refund.Status,
            Amount = refund.Amount
        };

    public static CardDetails ToCardDetails(CardRequestDto card)
    {
        var expiry = NormalizeExpiry(card.Expiry);
        var number = (card.Number ?? string.Empty).Replace(" ", string.Empty);
        return new CardDetails
        {
            Number = number,
            Expiry = expiry,
            SecurityCode = card.SecurityCode,
            Name = card.Name,
            BillingAddress = card.BillingAddress is null
                ? null
                : new CardBillingAddress
                {
                    AddressLine1 = card.BillingAddress.AddressLine1,
                    AddressLine2 = card.BillingAddress.AddressLine2,
                    AdminArea1 = card.BillingAddress.AdminArea1,
                    AdminArea2 = card.BillingAddress.AdminArea2,
                    PostalCode = card.BillingAddress.PostalCode,
                    CountryCode = string.IsNullOrWhiteSpace(card.BillingAddress.CountryCode)
                        ? "US"
                        : card.BillingAddress.CountryCode
                }
        };
    }

    public static Address? ToAddress(ShippingAddressRequest? shipTo)
    {
        if (shipTo is null || string.IsNullOrWhiteSpace(shipTo.Street))
        {
            return null;
        }

        return new Address(shipTo.Street, shipTo.City, shipTo.State, shipTo.Country, shipTo.ZipCode);
    }

    private static string NormalizeExpiry(string expiry)
    {
        var trimmed = (expiry ?? string.Empty).Trim();
        if (trimmed.Length == 7 && trimmed[4] == '-')
        {
            return trimmed;
        }

        var parts = trimmed.Split('/', '-', ' ');
        if (parts.Length == 2 && parts[0].Length is 1 or 2)
        {
            var month = int.Parse(parts[0]);
            var yearPart = parts[1];
            var year = yearPart.Length == 2 ? 2000 + int.Parse(yearPart) : int.Parse(yearPart);
            return $"{year:D4}-{month:D2}";
        }

        return trimmed;
    }
}

public class PaymentMethodResponse
{
    public int PaymentMethodId { get; set; }
    public string LastDigits { get; set; } = string.Empty;
    public string? Brand { get; set; }
    public string? Expiry { get; set; }
    public string? CardholderName { get; set; }

    public static PaymentMethodResponse Map(SavedPaymentMethod method) =>
        new()
        {
            PaymentMethodId = method.Id,
            LastDigits = method.LastDigits,
            Brand = method.Brand,
            Expiry = method.Expiry,
            CardholderName = method.CardholderName
        };
}
