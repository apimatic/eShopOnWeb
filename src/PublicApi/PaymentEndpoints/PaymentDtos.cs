using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.PaymentProcessing;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>
/// Raw card details as posted by a caller. Mapped straight to a transient <see cref="CardDetails"/>
/// and handed to PayPal; the number and security code are never persisted or logged.
/// </summary>
public class CardRequestDto
{
    public string Number { get; set; } = default!;
    public int ExpiryMonth { get; set; }
    public int ExpiryYear { get; set; }
    public string SecurityCode { get; set; } = default!;
    public string? CardholderName { get; set; }
    public string? CountryCode { get; set; }
    public string? AddressLine1 { get; set; }
    public string? AddressLine2 { get; set; }
    public string? AdminArea1 { get; set; }
    public string? AdminArea2 { get; set; }
    public string? PostalCode { get; set; }

    public CardDetails ToCardDetails() => new()
    {
        Number = Number,
        ExpiryMonth = ExpiryMonth,
        ExpiryYear = ExpiryYear,
        SecurityCode = SecurityCode,
        CardholderName = CardholderName,
        CountryCode = CountryCode,
        AddressLine1 = AddressLine1,
        AddressLine2 = AddressLine2,
        AdminArea1 = AdminArea1,
        AdminArea2 = AdminArea2,
        PostalCode = PostalCode
    };
}

public class AddressDto
{
    public string Street { get; set; } = default!;
    public string City { get; set; } = default!;
    public string State { get; set; } = default!;
    public string Country { get; set; } = default!;
    public string ZipCode { get; set; } = default!;

    public Address ToAddress() => new(Street, City, State, Country, ZipCode);
}

/// <summary>A safe, read-only view of an order's payment state.</summary>
public class PaymentStateDto
{
    public string? PayPalOrderId { get; set; }
    public string? AuthorizationId { get; set; }
    public string? AuthorizationStatus { get; set; }
    public decimal AuthorizedAmount { get; set; }
    public DateTimeOffset? AuthorizationExpiresAt { get; set; }
    public string? CaptureId { get; set; }
    public string? CaptureStatus { get; set; }
    public decimal? CapturedAmount { get; set; }
    public decimal? PayPalFee { get; set; }
    public decimal? NetAmount { get; set; }
    public decimal TotalRefunded { get; set; }
    public decimal RemainingRefundable { get; set; }
    public string Currency { get; set; } = default!;
    public List<RefundDto> Refunds { get; set; } = new();

    public static PaymentStateDto? From(Payment? payment)
    {
        if (payment is null)
        {
            return null;
        }
        return new PaymentStateDto
        {
            PayPalOrderId = payment.PayPalOrderId,
            AuthorizationId = payment.AuthorizationId,
            AuthorizationStatus = payment.AuthorizationStatus,
            AuthorizedAmount = payment.AuthorizedAmount,
            AuthorizationExpiresAt = payment.AuthorizationExpiresAt,
            CaptureId = payment.CaptureId,
            CaptureStatus = payment.CaptureStatus,
            CapturedAmount = payment.CapturedAmount,
            PayPalFee = payment.PayPalFee,
            NetAmount = payment.NetAmount,
            TotalRefunded = payment.TotalRefunded,
            RemainingRefundable = payment.RemainingRefundable,
            Currency = payment.Currency,
            Refunds = payment.Refunds
                .Select(r => new RefundDto { RefundId = r.RefundId, Status = r.Status, Amount = r.Amount })
                .ToList()
        };
    }
}

public class RefundDto
{
    public string RefundId { get; set; } = default!;
    public string Status { get; set; } = default!;
    public decimal Amount { get; set; }
}

/// <summary>An order plus its payment state, for GET /api/my-orders.</summary>
public class OrderSummaryDto
{
    public int OrderId { get; set; }
    public DateTimeOffset OrderDate { get; set; }
    public string Status { get; set; } = default!;
    public decimal Total { get; set; }
    public List<OrderItemDto> Items { get; set; } = new();
    public PaymentStateDto? Payment { get; set; }

    public static OrderSummaryDto From(Order order) => new()
    {
        OrderId = order.Id,
        OrderDate = order.OrderDate,
        Status = order.Status.ToString(),
        Total = order.Total(),
        Items = order.OrderItems.Select(i => new OrderItemDto
        {
            CatalogItemId = i.ItemOrdered.CatalogItemId,
            ProductName = i.ItemOrdered.ProductName,
            UnitPrice = i.UnitPrice,
            Units = i.Units
        }).ToList(),
        Payment = PaymentStateDto.From(order.Payment)
    };
}

public class OrderItemDto
{
    public int CatalogItemId { get; set; }
    public string ProductName { get; set; } = default!;
    public decimal UnitPrice { get; set; }
    public int Units { get; set; }
}
