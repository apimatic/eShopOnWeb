using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.PublicApi;

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
    public string AddressLine1 { get; set; } = string.Empty;
    public string? AddressLine2 { get; set; }
    public string AdminArea1 { get; set; } = string.Empty;
    public string AdminArea2 { get; set; } = string.Empty;
    public string PostalCode { get; set; } = string.Empty;
    public string CountryCode { get; set; } = string.Empty;
}

public class ShippingAddressRequest
{
    public string Street { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string Country { get; set; } = string.Empty;
    public string ZipCode { get; set; } = string.Empty;
}

internal static class PaymentRequestMapper
{
    public static CardPaymentDetails ToCardDetails(CardRequest? card)
    {
        if (card is null)
        {
            throw new CheckoutException(400, "Card details are required.", "INVALID_CARD");
        }

        var number = NormalizeCardNumber(card.Number);
        if (number.Length is < 13 or > 19)
        {
            throw new CheckoutException(400, "Card number is invalid.", "INVALID_CARD");
        }

        if (string.IsNullOrWhiteSpace(card.Expiry) || card.Expiry.Length != 7)
        {
            throw new CheckoutException(400, "Card expiry must be in YYYY-MM format.", "INVALID_CARD");
        }

        if (string.IsNullOrWhiteSpace(card.SecurityCode) || card.SecurityCode.Length is < 3 or > 4)
        {
            throw new CheckoutException(400, "Card security code is invalid.", "INVALID_CARD");
        }

        if (string.IsNullOrWhiteSpace(card.Name))
        {
            throw new CheckoutException(400, "Cardholder name is required.", "INVALID_CARD");
        }

        var billing = card.BillingAddress ?? throw new CheckoutException(400, "Billing address is required.", "INVALID_CARD");
        if (string.IsNullOrWhiteSpace(billing.AddressLine1) ||
            string.IsNullOrWhiteSpace(billing.AdminArea1) ||
            string.IsNullOrWhiteSpace(billing.AdminArea2) ||
            string.IsNullOrWhiteSpace(billing.PostalCode) ||
            string.IsNullOrWhiteSpace(billing.CountryCode))
        {
            throw new CheckoutException(400, "Billing address is incomplete.", "INVALID_CARD");
        }

        return new CardPaymentDetails(
            number,
            card.Expiry,
            card.SecurityCode,
            card.Name,
            new CardBillingAddress(
                billing.AddressLine1,
                billing.AddressLine2,
                billing.AdminArea1,
                billing.AdminArea2,
                billing.PostalCode,
                billing.CountryCode.ToUpperInvariant()));
    }

    public static Address ToShippingAddress(ShippingAddressRequest? shipping)
    {
        if (shipping is null)
        {
            return new Address("Not provided", "N/A", "N/A", "US", "00000");
        }

        return new Address(
            string.IsNullOrWhiteSpace(shipping.Street) ? "Not provided" : shipping.Street,
            string.IsNullOrWhiteSpace(shipping.City) ? "N/A" : shipping.City,
            string.IsNullOrWhiteSpace(shipping.State) ? "N/A" : shipping.State,
            string.IsNullOrWhiteSpace(shipping.Country) ? "US" : shipping.Country,
            string.IsNullOrWhiteSpace(shipping.ZipCode) ? "00000" : shipping.ZipCode);
    }

    public static OrderResponse ToOrderResponse(Order order) => new()
    {
        OrderId = order.Id,
        BuyerId = order.BuyerId,
        Status = order.Status.ToString(),
        Currency = order.Currency,
        Total = order.Total(),
        OrderDate = order.OrderDate,
        Items = order.OrderItems.Select(i => new OrderItemResponse
        {
            CatalogItemId = i.ItemOrdered.CatalogItemId,
            ProductName = i.ItemOrdered.ProductName,
            UnitPrice = i.UnitPrice,
            Quantity = i.Units
        }).ToList(),
        Payment = new PaymentStateResponse
        {
            PayPalOrderId = order.PayPalOrderId,
            InvoiceId = order.PayPalInvoiceId,
            AuthorizationId = order.PayPalAuthorizationId,
            AuthorizationStatus = order.PayPalAuthorizationStatus,
            AuthorizationExpirationTime = order.AuthorizationExpirationTime,
            CaptureId = order.PayPalCaptureId,
            CaptureStatus = order.PayPalCaptureStatus,
            CapturedAmount = order.CapturedAmount,
            PaypalFee = order.PayPalFee,
            NetAmount = order.NetAmount,
            RefundedAmount = order.RefundedTotal(),
            RemainingRefundable = order.RemainingRefundable(),
            Refunds = order.Refunds.Select(r => new RefundResponse
            {
                RefundId = r.Id,
                PayPalRefundId = r.PayPalRefundId,
                Status = r.Status,
                Amount = r.Amount,
                IdempotencyKey = r.IdempotencyKey,
                CreatedAt = r.CreatedAt
            }).ToList()
        }
    };

    public static SavedPaymentMethodResponse ToPaymentMethodResponse(SavedPaymentMethod method) => new()
    {
        PaymentMethodId = method.Id,
        Brand = method.Brand,
        Last4 = method.Last4,
        Expiry = method.Expiry,
        CardholderName = method.CardholderName
    };

    private static string NormalizeCardNumber(string? number)
    {
        if (string.IsNullOrWhiteSpace(number))
        {
            return string.Empty;
        }

        return new string(number.Where(char.IsDigit).ToArray());
    }
}

public class OrderResponse
{
    public int OrderId { get; set; }
    public string BuyerId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? Currency { get; set; }
    public decimal Total { get; set; }
    public DateTimeOffset OrderDate { get; set; }
    public List<OrderItemResponse> Items { get; set; } = new();
    public PaymentStateResponse Payment { get; set; } = new();
}

public class OrderItemResponse
{
    public int CatalogItemId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public int Quantity { get; set; }
}

public class PaymentStateResponse
{
    public string? PayPalOrderId { get; set; }
    public string? InvoiceId { get; set; }
    public string? AuthorizationId { get; set; }
    public string? AuthorizationStatus { get; set; }
    public DateTimeOffset? AuthorizationExpirationTime { get; set; }
    public string? CaptureId { get; set; }
    public string? CaptureStatus { get; set; }
    public decimal? CapturedAmount { get; set; }
    public decimal? PaypalFee { get; set; }
    public decimal? NetAmount { get; set; }
    public decimal RefundedAmount { get; set; }
    public decimal RemainingRefundable { get; set; }
    public List<RefundResponse> Refunds { get; set; } = new();
}

public class RefundResponse
{
    public int RefundId { get; set; }
    public string PayPalRefundId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}

public class SavedPaymentMethodResponse
{
    public int PaymentMethodId { get; set; }
    public string Brand { get; set; } = string.Empty;
    public string Last4 { get; set; } = string.Empty;
    public string Expiry { get; set; } = string.Empty;
    public string? CardholderName { get; set; }
}
