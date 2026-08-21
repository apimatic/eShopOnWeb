using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>Card details for a one-off payment or to save a card. Never stored or logged by this app.</summary>
public class CardRequestDto
{
    public string Number { get; set; } = string.Empty;
    public string Expiry { get; set; } = string.Empty;       // "YYYY-MM"
    public string SecurityCode { get; set; } = string.Empty;
    public string CardholderName { get; set; } = string.Empty;
    public string CountryCode { get; set; } = "US";
    public string? AddressLine1 { get; set; }
    public string? AddressLine2 { get; set; }
    public string? AdminArea1 { get; set; }   // state / province
    public string? AdminArea2 { get; set; }   // city
    public string? PostalCode { get; set; }

    public CardDetails ToCardDetails() => new(
        Number, Expiry, SecurityCode, CardholderName, CountryCode,
        AddressLine1, AddressLine2, AdminArea1, AdminArea2, PostalCode);
}

public class AddressDto
{
    public string Street { get; set; } = "N/A";
    public string City { get; set; } = "N/A";
    public string State { get; set; } = "N/A";
    public string Country { get; set; } = "US";
    public string ZipCode { get; set; } = "00000";

    public Address ToAddress() => new(Street, City, State, Country, ZipCode);
}

public class RefundDto
{
    public string RefundId { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTimeOffset CreatedDate { get; set; }
}

/// <summary>The payment state of an order, safe to return to the shopper.</summary>
public class PaymentStateDto
{
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string InvoiceReference { get; set; } = string.Empty;

    public string? PayPalOrderId { get; set; }
    public string? AuthorizationId { get; set; }
    public string? AuthorizationStatus { get; set; }
    public string? CaptureId { get; set; }
    public string? CaptureStatus { get; set; }
    public bool CapturePending { get; set; }

    public decimal? CapturedGross { get; set; }
    public decimal? PayPalFee { get; set; }
    public decimal? NetProceeds { get; set; }

    public decimal RefundedAmount { get; set; }
    public decimal RefundableRemaining { get; set; }
    public List<RefundDto> Refunds { get; set; } = new();

    public static PaymentStateDto From(Payment payment) => new()
    {
        OrderId = payment.OrderId,
        Status = payment.Status.ToString(),
        Amount = payment.Amount,
        Currency = payment.CurrencyCode,
        InvoiceReference = payment.InvoiceReference,
        PayPalOrderId = payment.PayPalOrderId,
        AuthorizationId = payment.AuthorizationId,
        AuthorizationStatus = payment.AuthorizationStatus,
        CaptureId = payment.CaptureId,
        CaptureStatus = payment.CaptureStatus,
        CapturePending = string.Equals(payment.CaptureStatus, "PENDING", StringComparison.OrdinalIgnoreCase),
        CapturedGross = payment.CapturedGross,
        PayPalFee = payment.CapturedFee,
        NetProceeds = payment.CapturedNet,
        RefundedAmount = payment.RefundedAmount(),
        RefundableRemaining = payment.RefundableRemaining(),
        Refunds = payment.Refunds
            .OrderBy(r => r.CreatedDate)
            .Select(r => new RefundDto
            {
                RefundId = r.PayPalRefundId,
                Amount = r.Amount,
                Status = r.Status,
                CreatedDate = r.CreatedDate
            })
            .ToList()
    };
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
    public decimal Total { get; set; }
    public List<OrderItemDto> Items { get; set; } = new();
    public PaymentStateDto Payment { get; set; } = new();

    public static OrderSummaryDto From(OrderWithPayment source)
    {
        var order = source.Order;
        return new OrderSummaryDto
        {
            OrderId = order.Id,
            OrderDate = order.OrderDate,
            Total = order.Total(),
            Items = order.OrderItems.Select(i => new OrderItemDto
            {
                CatalogItemId = i.ItemOrdered.CatalogItemId,
                ProductName = i.ItemOrdered.ProductName,
                UnitPrice = i.UnitPrice,
                Units = i.Units
            }).ToList(),
            Payment = PaymentStateDto.From(source.Payment)
        };
    }
}
