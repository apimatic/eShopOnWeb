using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

// ---- Requests ----

public class PlaceOrderRequest
{
    [Required]
    public List<OrderLineDto> Items { get; set; } = new();
    public AddressDto? ShipToAddress { get; set; }
}

public class OrderLineDto
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

public class AddressDto
{
    public string Street { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string Country { get; set; } = string.Empty;
    public string ZipCode { get; set; } = string.Empty;
}

public class CardDto
{
    [Required] public string Number { get; set; } = string.Empty;
    [Required] public string ExpiryMonth { get; set; } = string.Empty;
    [Required] public string ExpiryYear { get; set; } = string.Empty;
    [Required] public string SecurityCode { get; set; } = string.Empty;
    public string? CardholderName { get; set; }
    public AddressDto? BillingAddress { get; set; }
}

public class PayOrderRequest
{
    /// <summary>Card details for a one-off payment. Provide this OR <see cref="SavedPaymentMethodId"/>.</summary>
    public CardDto? Card { get; set; }

    /// <summary>Id of one of the caller's saved cards to pay with. Provide this OR <see cref="Card"/>.</summary>
    public int? SavedPaymentMethodId { get; set; }
}

public class RefundOrderRequest
{
    /// <summary>Amount to refund. Omit for a full refund of the remaining refundable amount.</summary>
    public decimal? Amount { get; set; }

    /// <summary>Caller-supplied idempotency key. Repeating a request under the same key never refunds twice.</summary>
    [Required]
    public string IdempotencyKey { get; set; } = string.Empty;
}

public class SavePaymentMethodRequest
{
    [Required] public CardDto Card { get; set; } = new();
    public string? Label { get; set; }
}

// ---- Responses ----

public class RefundDto
{
    public int Id { get; set; }
    public string PayPalRefundId { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}

public class PaymentStateDto
{
    public string Status { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
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
    public decimal RefundableAmount { get; set; }
    public List<RefundDto> Refunds { get; set; } = new();

    public static PaymentStateDto? From(Payment? payment)
    {
        if (payment is null) return null;
        return new PaymentStateDto
        {
            Status = payment.Status.ToString(),
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
            RefundableAmount = payment.RefundableAmount,
            Refunds = payment.Refunds
                .OrderBy(r => r.Id)
                .Select(r => new RefundDto
                {
                    Id = r.Id,
                    PayPalRefundId = r.PayPalRefundId,
                    Amount = r.Amount,
                    Status = r.Status,
                    CreatedAt = r.CreatedAt
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

public class OrderSummaryDto
{
    public int OrderId { get; set; }
    public DateTimeOffset OrderDate { get; set; }
    public string OrderStatus { get; set; } = string.Empty;
    public decimal Total { get; set; }
    public List<OrderItemDto> Items { get; set; } = new();
    public PaymentStateDto? Payment { get; set; }

    public static OrderSummaryDto From(Order order, Payment? payment) => new()
    {
        OrderId = order.Id,
        OrderDate = order.OrderDate,
        OrderStatus = order.Status.ToString(),
        Total = order.Total(),
        Items = order.OrderItems.Select(i => new OrderItemDto
        {
            CatalogItemId = i.ItemOrdered.CatalogItemId,
            ProductName = i.ItemOrdered.ProductName,
            UnitPrice = i.UnitPrice,
            Units = i.Units
        }).ToList(),
        Payment = PaymentStateDto.From(payment)
    };
}

public class PlaceOrderResponse
{
    /// <summary>The created order's identifier (top-level, so the flow can be driven end to end).</summary>
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public decimal Total { get; set; }
    public string Currency { get; set; } = string.Empty;
}

public class PayOrderResponse
{
    public int OrderId { get; set; }
    public PaymentStateDto Payment { get; set; } = new();
}

public class RefundResponse
{
    /// <summary>The created refund's identifier (top-level).</summary>
    public int RefundId { get; set; }
    public string PayPalRefundId { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Status { get; set; } = string.Empty;
    public PaymentStateDto Payment { get; set; } = new();
}

public class SavePaymentMethodResponse
{
    /// <summary>The saved card's identifier (top-level).</summary>
    public int PaymentMethodId { get; set; }
    public string? Brand { get; set; }
    public string LastFourDigits { get; set; } = string.Empty;
    public string? ExpiryMonth { get; set; }
    public string? ExpiryYear { get; set; }
    public string? Label { get; set; }
}

public class SavedCardDto
{
    public int Id { get; set; }
    public string? Brand { get; set; }
    public string LastFourDigits { get; set; } = string.Empty;
    public string? ExpiryMonth { get; set; }
    public string? ExpiryYear { get; set; }
    public string? Label { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public static SavedCardDto From(SavedPaymentMethod card) => new()
    {
        Id = card.Id,
        Brand = card.CardBrand,
        LastFourDigits = card.LastFourDigits,
        ExpiryMonth = card.ExpiryMonth,
        ExpiryYear = card.ExpiryYear,
        Label = card.Label,
        CreatedAt = card.CreatedAt
    };
}
