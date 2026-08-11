using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

// ---- Requests -------------------------------------------------------------------------------
// Server-set fields (BuyerId from the token, OrderId from the route) are [JsonIgnore] so a caller
// can never supply them in the body; the endpoint populates them.

public class CreateOrderRequest
{
    public List<OrderLineRequest> Items { get; set; } = new();
    public AddressRequest? ShipToAddress { get; set; }

    [JsonIgnore] public string BuyerId { get; set; } = default!;
}

/// <summary>An operator action targeting one order by id (fulfil / cancel).</summary>
public class OrderOperationRequest
{
    [JsonIgnore] public int OrderId { get; set; }
}

public class MyOrdersRequest
{
    [JsonIgnore] public string BuyerId { get; set; } = default!;
}

public class OrderLineRequest
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

public class AddressRequest
{
    public string Street { get; set; } = default!;
    public string City { get; set; } = default!;
    public string? State { get; set; }
    public string Country { get; set; } = default!;
    public string ZipCode { get; set; } = default!;

    public Address ToAddress() => new(Street, City, State ?? string.Empty, Country, ZipCode);
}

/// <summary>Pay a card either directly (Card) or with a previously saved card (SavedPaymentMethodId).</summary>
public class PayOrderRequest
{
    public CardRequestDto? Card { get; set; }
    public int? SavedPaymentMethodId { get; set; }

    [JsonIgnore] public string BuyerId { get; set; } = default!;
    [JsonIgnore] public int OrderId { get; set; }
}

public class CardRequestDto
{
    public string Number { get; set; } = default!;
    public string ExpiryMonth { get; set; } = default!;
    public string ExpiryYear { get; set; } = default!;
    public string SecurityCode { get; set; } = default!;
    public string? CardholderName { get; set; }
    public string? BillingAddressLine1 { get; set; }
    public string? BillingCity { get; set; }
    public string? BillingState { get; set; }
    public string? BillingPostalCode { get; set; }
    public string? BillingCountryCode { get; set; }

    public CardDetails ToCardDetails() => new()
    {
        Number = Number,
        ExpiryMonth = ExpiryMonth,
        ExpiryYear = ExpiryYear,
        SecurityCode = SecurityCode,
        CardholderName = CardholderName,
        BillingAddressLine1 = BillingAddressLine1,
        BillingAdminArea2 = BillingCity,
        BillingAdminArea1 = BillingState,
        BillingPostalCode = BillingPostalCode,
        BillingCountryCode = BillingCountryCode
    };
}

public class RefundOrderRequest
{
    public decimal? Amount { get; set; }

    /// <summary>Caller-supplied idempotency key; repeating a request under the same key never refunds twice.</summary>
    public string IdempotencyKey { get; set; } = default!;

    [JsonIgnore] public string BuyerId { get; set; } = default!;
    [JsonIgnore] public int OrderId { get; set; }
}

// ---- Responses ------------------------------------------------------------------------------

public class CreateOrderResponse
{
    public int OrderId { get; set; }
    public string Status { get; set; } = default!;
    public decimal Total { get; set; }
    public string Currency { get; set; } = default!;
    public OrderSummaryDto Order { get; set; } = default!;
}

public class RefundOrderResponse
{
    public string RefundId { get; set; } = default!;
    public string Status { get; set; } = default!;
    public decimal Amount { get; set; }
    public OrderSummaryDto Order { get; set; } = default!;
}

public class OrderSummaryDto
{
    public int OrderId { get; set; }
    public string Status { get; set; } = default!;
    public DateTimeOffset OrderDate { get; set; }
    public decimal Total { get; set; }
    public string Currency { get; set; } = default!;
    public List<OrderItemDto> Items { get; set; } = new();
    public PaymentDto? Payment { get; set; }
}

public class OrderItemDto
{
    public int CatalogItemId { get; set; }
    public string ProductName { get; set; } = default!;
    public decimal UnitPrice { get; set; }
    public int Units { get; set; }
}

public class PaymentDto
{
    public decimal Amount { get; set; }
    public string Currency { get; set; } = default!;
    public string? PayPalOrderId { get; set; }
    public string? AuthorizationId { get; set; }
    public string? AuthorizationStatus { get; set; }
    public DateTimeOffset? AuthorizationExpiresAt { get; set; }
    public string? CaptureId { get; set; }
    public string? CaptureStatus { get; set; }
    public decimal? CapturedAmount { get; set; }
    public decimal? PayPalFee { get; set; }
    public decimal? NetAmount { get; set; }
    public decimal TotalRefunded { get; set; }
    public decimal RefundableRemaining { get; set; }
    public List<RefundDto> Refunds { get; set; } = new();
}

public class RefundDto
{
    public string RefundId { get; set; } = default!;
    public decimal Amount { get; set; }
    public string Status { get; set; } = default!;
    public DateTimeOffset CreatedAt { get; set; }
}

public static class OrderMapper
{
    public static OrderSummaryDto ToSummary(Order order, string fallbackCurrency)
    {
        return new OrderSummaryDto
        {
            OrderId = order.Id,
            Status = order.Status.ToString(),
            OrderDate = order.OrderDate,
            Total = order.Total(),
            Currency = order.Payment?.Currency ?? fallbackCurrency,
            Items = order.OrderItems.Select(i => new OrderItemDto
            {
                CatalogItemId = i.ItemOrdered.CatalogItemId,
                ProductName = i.ItemOrdered.ProductName,
                UnitPrice = i.UnitPrice,
                Units = i.Units
            }).ToList(),
            Payment = order.Payment is null ? null : ToPayment(order.Payment)
        };
    }

    private static PaymentDto ToPayment(OrderPayment payment) => new()
    {
        Amount = payment.Amount,
        Currency = payment.Currency,
        PayPalOrderId = payment.PayPalOrderId,
        AuthorizationId = payment.AuthorizationId,
        AuthorizationStatus = payment.AuthorizationStatus,
        AuthorizationExpiresAt = payment.AuthorizationExpiresAt,
        CaptureId = payment.CaptureId,
        CaptureStatus = payment.CaptureStatus,
        CapturedAmount = payment.CapturedAmount,
        PayPalFee = payment.PayPalFee,
        NetAmount = payment.NetAmount,
        TotalRefunded = payment.TotalRefunded,
        RefundableRemaining = payment.RefundableRemaining,
        Refunds = payment.Refunds.Select(r => new RefundDto
        {
            RefundId = r.RefundId,
            Amount = r.Amount,
            Status = r.Status,
            CreatedAt = r.CreatedAt
        }).ToList()
    };
}
