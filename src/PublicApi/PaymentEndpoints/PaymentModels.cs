using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.PayPal;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

// ---- Requests ----

public class PlaceOrderRequest
{
    public List<OrderLineRequest> Items { get; set; } = new();
    public ShippingAddressRequest? ShipToAddress { get; set; }
}

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

/// <summary>Card details for a one-off payment or to be vaulted. Never stored or logged by this app.</summary>
public class CardRequest
{
    public string Number { get; set; } = string.Empty;
    public string ExpiryMonth { get; set; } = string.Empty;
    public string ExpiryYear { get; set; } = string.Empty;
    public string SecurityCode { get; set; } = string.Empty;
    public string CardholderName { get; set; } = string.Empty;
    public BillingAddressRequest? BillingAddress { get; set; }
}

public class BillingAddressRequest
{
    public string AddressLine1 { get; set; } = string.Empty;
    public string? AddressLine2 { get; set; }
    public string City { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string PostalCode { get; set; } = string.Empty;
    public string CountryCode { get; set; } = string.Empty;
}

/// <summary>Pay with EITHER a saved card (by id) OR one-off card details.</summary>
public class PayOrderRequest
{
    public int? SavedCardId { get; set; }
    public CardRequest? Card { get; set; }
}

public class RefundOrderRequest
{
    public decimal? Amount { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
}

// ---- Responses ----

public class PlaceOrderResponse
{
    public int OrderId { get; set; }
    public decimal Total { get; set; }
    public string Status { get; set; } = string.Empty;
    public List<OrderLineDto> Items { get; set; } = new();
}

public class OrderLineDto
{
    public int CatalogItemId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public int Units { get; set; }
}

public class RefundResponse
{
    public string RefundId { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Status { get; set; } = string.Empty;
}

public class PaymentDto
{
    public string OrderStatus { get; set; } = string.Empty;
    public string CurrencyCode { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string PaymentStatus { get; set; } = string.Empty;
    public string PayPalOrderId { get; set; } = string.Empty;
    public string? AuthorizationId { get; set; }
    public DateTimeOffset? AuthorizationExpiresAt { get; set; }
    public string? CaptureId { get; set; }
    public decimal? CapturedAmount { get; set; }
    public decimal? PayPalFee { get; set; }
    public decimal? NetAmount { get; set; }
    public decimal TotalRefunded { get; set; }
    public decimal RefundableRemaining { get; set; }
    public List<RefundDto> Refunds { get; set; } = new();
}

public class RefundDto
{
    public string RefundId { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Status { get; set; } = string.Empty;
}

public class OrderPaymentResponse
{
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public PaymentDto? Payment { get; set; }
}

public class MyOrdersResponse
{
    public List<MyOrderDto> Orders { get; set; } = new();
}

public class MyOrderDto
{
    public int OrderId { get; set; }
    public DateTimeOffset OrderDate { get; set; }
    public decimal Total { get; set; }
    public string PaymentStatus { get; set; } = string.Empty;
    public List<OrderLineDto> Items { get; set; } = new();
    public PaymentDto? Payment { get; set; }
}

/// <summary>Mapping helpers between domain entities and API DTOs.</summary>
public static class PaymentMapping
{
    /// <summary>The identity name carried in the JWT — the buyer id used to scope every shopper action.</summary>
    public static string? BuyerId(this ClaimsPrincipal user) =>
        user.FindFirstValue(ClaimTypes.Name) ?? user.Identity?.Name;

    public static CardDetails ToCardDetails(this CardRequest card) => new(
        Number: card.Number,
        ExpiryMonth: card.ExpiryMonth,
        ExpiryYear: card.ExpiryYear,
        SecurityCode: card.SecurityCode,
        CardholderName: card.CardholderName,
        BillingAddress: card.BillingAddress is null ? null : new BillingAddress(
            AddressLine1: card.BillingAddress.AddressLine1,
            AddressLine2: card.BillingAddress.AddressLine2,
            AdminArea2: card.BillingAddress.City,
            AdminArea1: card.BillingAddress.State,
            PostalCode: card.BillingAddress.PostalCode,
            CountryCode: card.BillingAddress.CountryCode));

    public static List<OrderLineDto> ToLineDtos(this Order order) =>
        order.OrderItems.Select(i => new OrderLineDto
        {
            CatalogItemId = i.ItemOrdered.CatalogItemId,
            ProductName = i.ItemOrdered.ProductName,
            UnitPrice = i.UnitPrice,
            Units = i.Units
        }).ToList();

    /// <summary>The order-level status a shopper sees, derived from the payment (or lack of one).</summary>
    public static string OrderStatus(Payment? payment) => payment?.Status switch
    {
        null => "AwaitingPayment",
        PaymentStatus.Authorized => "Authorized",
        PaymentStatus.Captured => "Fulfilled",
        PaymentStatus.PartiallyRefunded => "PartiallyRefunded",
        PaymentStatus.Refunded => "Refunded",
        PaymentStatus.Voided => "Cancelled",
        _ => payment.Status.ToString()
    };

    public static PaymentDto? ToPaymentDto(Payment? payment)
    {
        if (payment is null)
        {
            return null;
        }

        return new PaymentDto
        {
            OrderStatus = OrderStatus(payment),
            CurrencyCode = payment.CurrencyCode,
            Amount = payment.Amount,
            PaymentStatus = payment.Status.ToString(),
            PayPalOrderId = payment.PayPalOrderId,
            AuthorizationId = payment.AuthorizationId,
            AuthorizationExpiresAt = payment.AuthorizationExpiresAt,
            CaptureId = payment.CaptureId,
            CapturedAmount = payment.CapturedAmount,
            PayPalFee = payment.PayPalFee,
            NetAmount = payment.NetAmount,
            TotalRefunded = payment.TotalRefunded,
            RefundableRemaining = payment.RefundableRemaining,
            Refunds = payment.Refunds.Select(r => new RefundDto
            {
                RefundId = r.PayPalRefundId,
                Amount = r.Amount,
                Status = r.Status
            }).ToList()
        };
    }
}
