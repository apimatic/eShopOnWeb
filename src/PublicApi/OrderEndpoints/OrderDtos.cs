using System.Collections.Generic;
using System.Linq;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class OrderResponse
{
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public decimal Total { get; set; }
    public string? Currency { get; set; }
    public string BuyerId { get; set; } = string.Empty;
    public System.DateTimeOffset OrderDate { get; set; }
    public PaymentStateDto Payment { get; set; } = new();
    public List<OrderItemDto> Items { get; set; } = new();
    public List<RefundDto> Refunds { get; set; } = new();

    public static OrderResponse From(Order order)
    {
        return new OrderResponse
        {
            OrderId = order.Id,
            Status = order.Status.ToString(),
            Total = order.Total(),
            Currency = order.Currency,
            BuyerId = order.BuyerId,
            OrderDate = order.OrderDate,
            Payment = new PaymentStateDto
            {
                PayPalOrderId = order.PayPalOrderId,
                AuthorizationId = order.PayPalAuthorizationId,
                AuthorizationStatus = order.PayPalAuthorizationStatus,
                AuthorizationExpiresAt = order.AuthorizationExpiresAt,
                CaptureId = order.PayPalCaptureId,
                CaptureStatus = order.PayPalCaptureStatus,
                CapturedAmount = order.CapturedAmount,
                PayPalFee = order.PayPalFee,
                NetAmount = order.NetAmount,
                RemainingRefundable = order.RemainingRefundable()
            },
            Items = order.OrderItems.Select(i => new OrderItemDto
            {
                CatalogItemId = i.ItemOrdered.CatalogItemId,
                ProductName = i.ItemOrdered.ProductName,
                UnitPrice = i.UnitPrice,
                Units = i.Units
            }).ToList(),
            Refunds = order.Refunds.Select(RefundDto.From).ToList()
        };
    }
}

public class PaymentStateDto
{
    public string? PayPalOrderId { get; set; }
    public string? AuthorizationId { get; set; }
    public string? AuthorizationStatus { get; set; }
    public System.DateTimeOffset? AuthorizationExpiresAt { get; set; }
    public string? CaptureId { get; set; }
    public string? CaptureStatus { get; set; }
    public decimal? CapturedAmount { get; set; }
    public decimal? PayPalFee { get; set; }
    public decimal? NetAmount { get; set; }
    public decimal RemainingRefundable { get; set; }
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
    public string Status { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;

    public static RefundDto From(OrderRefund refund)
    {
        return new RefundDto
        {
            RefundId = refund.Id,
            PayPalRefundId = refund.PayPalRefundId,
            Status = refund.Status,
            Amount = refund.Amount,
            IdempotencyKey = refund.IdempotencyKey
        };
    }
}

public class CardRequest
{
    public string Number { get; set; } = string.Empty;
    public string Expiry { get; set; } = string.Empty;
    public string SecurityCode { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public BillingAddressRequest? BillingAddress { get; set; }
}

public class BillingAddressRequest
{
    public string CountryCode { get; set; } = string.Empty;
    public string? AddressLine1 { get; set; }
    public string? AddressLine2 { get; set; }
    public string? AdminArea2 { get; set; }
    public string? AdminArea1 { get; set; }
    public string? PostalCode { get; set; }
}

public class ShipToAddressRequest
{
    public string Street { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string Country { get; set; } = string.Empty;
    public string ZipCode { get; set; } = string.Empty;
}

public static class PaymentRequestMapping
{
    public static Microsoft.eShopWeb.ApplicationCore.Interfaces.CardPaymentRequest ToCardPayment(CardRequest card)
    {
        return new Microsoft.eShopWeb.ApplicationCore.Interfaces.CardPaymentRequest(
            card.Number,
            card.Expiry,
            card.SecurityCode,
            card.Name,
            card.BillingAddress is null
                ? null
                : new Microsoft.eShopWeb.ApplicationCore.Interfaces.CardBillingAddressRequest(
                    card.BillingAddress.CountryCode,
                    card.BillingAddress.AddressLine1,
                    card.BillingAddress.AddressLine2,
                    card.BillingAddress.AdminArea2,
                    card.BillingAddress.AdminArea1,
                    card.BillingAddress.PostalCode));
    }

    public static Address ToAddress(ShipToAddressRequest? address)
    {
        if (address is null || string.IsNullOrWhiteSpace(address.Street))
        {
            return new Address("1 Microsoft Way", "Redmond", "WA", "US", "98052");
        }

        return new Address(address.Street, address.City, address.State, address.Country, address.ZipCode);
    }
}

public class SavedPaymentMethodResponse
{
    public int PaymentMethodId { get; set; }
    public string Brand { get; set; } = string.Empty;
    public string LastDigits { get; set; } = string.Empty;
    public string Expiry { get; set; } = string.Empty;

    public static SavedPaymentMethodResponse From(SavedPaymentMethod method)
    {
        return new SavedPaymentMethodResponse
        {
            PaymentMethodId = method.Id,
            Brand = method.Brand,
            LastDigits = method.LastDigits,
            Expiry = method.Expiry
        };
    }
}
