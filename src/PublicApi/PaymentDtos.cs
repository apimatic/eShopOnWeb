using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;
using Microsoft.eShopWeb.ApplicationCore.PayPal;

namespace Microsoft.eShopWeb.PublicApi;

internal static class PaymentMapping
{
    public static CardPaymentSource ToCardPaymentSource(CardRequest card)
    {
        return new CardPaymentSource(
            card.Number,
            card.Expiry,
            card.SecurityCode,
            card.Name,
            card.BillingAddress is null
                ? null
                : new CardBillingAddress(
                    card.BillingAddress.CountryCode,
                    card.BillingAddress.AddressLine1,
                    card.BillingAddress.AddressLine2,
                    card.BillingAddress.AdminArea2,
                    card.BillingAddress.AdminArea1,
                    card.BillingAddress.PostalCode));
    }

    public static Address? ToAddress(ShippingAddressRequest? shipping)
    {
        if (shipping is null)
        {
            return null;
        }

        return new Address(
            shipping.Street ?? "123 Main Street",
            shipping.City ?? "Seattle",
            shipping.State ?? "WA",
            shipping.Country ?? "US",
            shipping.ZipCode ?? "98101");
    }
}

public class CardRequest
{
    public string Number { get; set; } = string.Empty;
    public string Expiry { get; set; } = string.Empty;
    public string? SecurityCode { get; set; }
    public string? Name { get; set; }
    public BillingAddressRequest? BillingAddress { get; set; }
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

public class ShippingAddressRequest
{
    public string? Street { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? Country { get; set; }
    public string? ZipCode { get; set; }
}

public class OrderLineDto
{
    public int CatalogItemId { get; set; }
    public string? ProductName { get; set; }
    public decimal UnitPrice { get; set; }
    public int Quantity { get; set; }
}

public class RefundDto
{
    public int RefundId { get; set; }
    public string PayPalRefundId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string IdempotencyKey { get; set; } = string.Empty;
}

public class PaymentDto
{
    public string Status { get; set; } = string.Empty;
    public string Currency { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string? PayPalOrderId { get; set; }
    public string? AuthorizationId { get; set; }
    public string? AuthorizationStatus { get; set; }
    public DateTimeOffset? AuthorizationExpiration { get; set; }
    public string? CaptureId { get; set; }
    public string? CaptureStatus { get; set; }
    public decimal? CapturedAmount { get; set; }
    public decimal? PaypalFee { get; set; }
    public decimal? NetAmount { get; set; }
    public decimal RefundedAmount { get; set; }
    public decimal RemainingRefundable { get; set; }
    public List<RefundDto> Refunds { get; set; } = new();
}

public class OrderDto
{
    public int OrderId { get; set; }
    public string BuyerId { get; set; } = string.Empty;
    public DateTimeOffset OrderDate { get; set; }
    public decimal Total { get; set; }
    public List<OrderLineDto> Items { get; set; } = new();
    public PaymentDto? Payment { get; set; }
}

public class PaymentMethodDto
{
    public int PaymentMethodId { get; set; }
    public string? Brand { get; set; }
    public string? LastDigits { get; set; }
    public string? Expiry { get; set; }
    public string? CardholderName { get; set; }
}

internal static class PaymentDtoFactory
{
    public static OrderDto From(Order order, OrderPayment? payment)
    {
        return new OrderDto
        {
            OrderId = order.Id,
            BuyerId = order.BuyerId,
            OrderDate = order.OrderDate,
            Total = order.Total(),
            Items = order.OrderItems.Select(item => new OrderLineDto
            {
                CatalogItemId = item.ItemOrdered.CatalogItemId,
                ProductName = item.ItemOrdered.ProductName,
                UnitPrice = item.UnitPrice,
                Quantity = item.Units
            }).ToList(),
            Payment = payment is null ? null : From(payment)
        };
    }

    public static PaymentDto From(OrderPayment payment)
    {
        return new PaymentDto
        {
            Status = payment.Status.ToString(),
            Currency = payment.Currency,
            Amount = payment.Amount,
            PayPalOrderId = payment.PayPalOrderId,
            AuthorizationId = payment.AuthorizationId,
            AuthorizationStatus = payment.AuthorizationStatus,
            AuthorizationExpiration = payment.AuthorizationExpiration,
            CaptureId = payment.CaptureId,
            CaptureStatus = payment.CaptureStatus,
            CapturedAmount = payment.CapturedAmount,
            PaypalFee = payment.PaypalFee,
            NetAmount = payment.NetAmount,
            RefundedAmount = payment.RefundedAmount,
            RemainingRefundable = payment.RemainingRefundable,
            Refunds = payment.Refunds.Select(From).ToList()
        };
    }

    public static RefundDto From(OrderRefund refund)
    {
        return new RefundDto
        {
            RefundId = refund.Id,
            PayPalRefundId = refund.PayPalRefundId,
            Status = refund.Status,
            Amount = refund.Amount,
            Currency = refund.Currency,
            IdempotencyKey = refund.IdempotencyKey
        };
    }

    public static PaymentMethodDto From(SavedPaymentMethod method)
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
}
