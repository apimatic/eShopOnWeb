using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

// ----- Request payloads shared across order endpoints -----

/// <summary>Card details for a one-off payment. Never stored or logged by this app.</summary>
public class CardDto
{
    public string Number { get; set; } = default!;
    public string ExpiryMonth { get; set; } = default!; // MM
    public string ExpiryYear { get; set; } = default!;  // YYYY
    public string SecurityCode { get; set; } = default!;
    public string CardholderName { get; set; } = default!;
    public BillingAddressDto? BillingAddress { get; set; }
}

public class BillingAddressDto
{
    public string AddressLine1 { get; set; } = default!;
    public string? AddressLine2 { get; set; }
    public string City { get; set; } = default!;
    public string? State { get; set; }
    public string PostalCode { get; set; } = default!;
    public string CountryCode { get; set; } = default!; // ISO 3166-1 alpha-2
}

public class ShippingAddressDto
{
    public string Street { get; set; } = default!;
    public string City { get; set; } = default!;
    public string State { get; set; } = default!;
    public string Country { get; set; } = default!;
    public string ZipCode { get; set; } = default!;
}

// ----- Response payloads -----

public class OrderDto
{
    public int OrderId { get; set; }
    public string Status { get; set; } = default!;
    public decimal Total { get; set; }
    public string? Currency { get; set; }
    public DateTimeOffset OrderDate { get; set; }
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
    public string PayPalOrderId { get; set; } = default!;
    public string Currency { get; set; } = default!;
    public decimal Amount { get; set; }

    /// <summary>Human-readable payment state (Authorized, Captured, PartiallyRefunded, Refunded, Voided).</summary>
    public string State { get; set; } = default!;

    public string? AuthorizationId { get; set; }
    public string? AuthorizationStatus { get; set; }

    public string? CaptureId { get; set; }
    public string? CaptureStatus { get; set; }
    public decimal? CapturedAmount { get; set; }
    public decimal? PayPalFee { get; set; }
    public decimal? NetAmount { get; set; }

    public decimal RefundedAmount { get; set; }
    public decimal RefundableAmount { get; set; }
    public List<RefundDto> Refunds { get; set; } = new();
}

public class RefundDto
{
    public string RefundId { get; set; } = default!;
    public decimal Amount { get; set; }
    public string Status { get; set; } = default!;
    public DateTimeOffset CreatedDate { get; set; }
}

/// <summary>Maps order/payment domain entities onto the API response shape.</summary>
public static class OrderPaymentMapper
{
    public static OrderDto ToDto(Order order) => new()
    {
        OrderId = order.Id,
        Status = order.Status.ToString(),
        Total = order.Total(),
        Currency = order.Payment?.Currency,
        OrderDate = order.OrderDate,
        Items = order.OrderItems.Select(i => new OrderItemDto
        {
            CatalogItemId = i.ItemOrdered.CatalogItemId,
            ProductName = i.ItemOrdered.ProductName,
            UnitPrice = i.UnitPrice,
            Units = i.Units
        }).ToList(),
        Payment = order.Payment is null ? null : ToDto(order.Payment)
    };

    public static PaymentDto ToDto(Payment payment) => new()
    {
        PayPalOrderId = payment.PayPalOrderId,
        Currency = payment.Currency,
        Amount = payment.Amount,
        State = DescribeState(payment),
        AuthorizationId = payment.AuthorizationId,
        AuthorizationStatus = payment.AuthorizationStatus,
        CaptureId = payment.CaptureId,
        CaptureStatus = payment.CaptureStatus,
        CapturedAmount = payment.CapturedAmount,
        PayPalFee = payment.PayPalFee,
        NetAmount = payment.NetAmount,
        RefundedAmount = payment.RefundedAmount,
        RefundableAmount = payment.RefundableAmount,
        Refunds = payment.Refunds.Select(r => new RefundDto
        {
            RefundId = r.RefundId,
            Amount = r.Amount,
            Status = r.Status,
            CreatedDate = r.CreatedDate
        }).ToList()
    };

    private static string DescribeState(Payment payment)
    {
        if (string.Equals(payment.AuthorizationStatus, "VOIDED", StringComparison.OrdinalIgnoreCase))
            return "Voided";
        if (!payment.IsCaptured)
            return "Authorized";
        if (payment.RefundedAmount <= 0m)
            return "Captured";
        return payment.RefundableAmount <= 0m ? "Refunded" : "PartiallyRefunded";
    }
}
