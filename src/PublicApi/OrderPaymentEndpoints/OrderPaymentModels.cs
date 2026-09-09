using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.PublicApi.OrderPaymentEndpoints;

/// <summary>Maps the API card payload to the gateway card record.</summary>
public static class CardMapping
{
    public static PayPalCard ToPayPalCard(this CardDetailsDto dto)
    {
        PayPalBillingAddress? billing = dto.BillingAddress is null
            ? null
            : new PayPalBillingAddress(
                dto.BillingAddress.CountryCode,
                dto.BillingAddress.AddressLine1,
                dto.BillingAddress.AdminArea2,
                dto.BillingAddress.AdminArea1,
                dto.BillingAddress.PostalCode);

        return new PayPalCard(dto.Number, dto.Expiry, dto.SecurityCode, dto.Name, billing);
    }
}

// ---- Request payloads --------------------------------------------------------------------------

/// <summary>A card supplied for a one-off payment or to save. Full details are never stored or logged.</summary>
public class CardDetailsDto
{
    public string Number { get; set; } = string.Empty;
    /// <summary>Expiry in YYYY-MM (for example, 2030-01).</summary>
    public string Expiry { get; set; } = string.Empty;
    public string SecurityCode { get; set; } = string.Empty;
    public string? Name { get; set; }
    public BillingAddressDto? BillingAddress { get; set; }
}

public class BillingAddressDto
{
    public string CountryCode { get; set; } = "US";
    public string? AddressLine1 { get; set; }
    public string? AdminArea2 { get; set; }
    public string? AdminArea1 { get; set; }
    public string? PostalCode { get; set; }
}

public class ShipToAddressDto
{
    public string Street { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string Country { get; set; } = string.Empty;
    public string ZipCode { get; set; } = string.Empty;
}

public class OrderLineDto
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

// ---- Response payloads -------------------------------------------------------------------------

public class OrderPaymentResponse
{
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public decimal Total { get; set; }
    public string Currency { get; set; } = string.Empty;
    public DateTimeOffset OrderDate { get; set; }
    public List<OrderLineView> Items { get; set; } = new();
    public PaymentView? Payment { get; set; }
}

public class OrderLineView
{
    public int CatalogItemId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public int Units { get; set; }
}

public class PaymentView
{
    public string Status { get; set; } = string.Empty;
    public string SourceType { get; set; } = string.Empty;
    public string? CardBrand { get; set; }
    public string? CardLast4 { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string InvoiceId { get; set; } = string.Empty;

    public string PayPalOrderId { get; set; } = string.Empty;
    public string AuthorizationId { get; set; } = string.Empty;
    public string AuthorizationStatus { get; set; } = string.Empty;
    public DateTimeOffset? AuthorizationExpiresAt { get; set; }

    public string? CaptureId { get; set; }
    public string? CaptureStatus { get; set; }
    public decimal? CapturedAmount { get; set; }
    public decimal? PayPalFee { get; set; }
    public decimal? NetAmount { get; set; }
    public DateTimeOffset? CapturedAt { get; set; }

    public decimal RefundedTotal { get; set; }
    public decimal RefundableRemaining { get; set; }
    public List<RefundView> Refunds { get; set; } = new();
}

public class RefundView
{
    public string RefundId { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>Maps the order aggregate (with its payment) to the API response shape.</summary>
public static class OrderPaymentMapping
{
    public static OrderPaymentResponse ToResponse(Order order)
    {
        return new OrderPaymentResponse
        {
            OrderId = order.Id,
            Status = OrderStatus(order),
            Total = order.Total(),
            Currency = order.Payment?.CurrencyCode ?? string.Empty,
            OrderDate = order.OrderDate,
            Items = order.OrderItems.Select(i => new OrderLineView
            {
                CatalogItemId = i.ItemOrdered.CatalogItemId,
                ProductName = i.ItemOrdered.ProductName,
                UnitPrice = i.UnitPrice,
                Units = i.Units
            }).ToList(),
            Payment = order.Payment is null ? null : ToPaymentView(order.Payment)
        };
    }

    private static PaymentView ToPaymentView(Payment p) => new()
    {
        Status = p.Status.ToString(),
        SourceType = p.SourceType.ToString(),
        CardBrand = p.CardBrand,
        CardLast4 = p.CardLast4,
        Amount = p.Amount,
        Currency = p.CurrencyCode,
        InvoiceId = p.InvoiceId,
        PayPalOrderId = p.PayPalOrderId,
        AuthorizationId = p.AuthorizationId,
        AuthorizationStatus = p.AuthorizationStatus,
        AuthorizationExpiresAt = p.AuthorizationExpiresAt,
        CaptureId = p.CaptureId,
        CaptureStatus = p.CaptureStatus,
        CapturedAmount = p.CapturedAmount,
        PayPalFee = p.PayPalFee,
        NetAmount = p.NetAmount,
        CapturedAt = p.CapturedAt,
        RefundedTotal = p.TotalRefunded(),
        RefundableRemaining = p.RefundableRemaining(),
        Refunds = p.Refunds.Select(r => new RefundView
        {
            RefundId = r.RefundId,
            Amount = r.Amount,
            Status = r.Status,
            CreatedAt = r.CreatedAt
        }).ToList()
    };

    /// <summary>A single, shopper-friendly order status derived from the payment lifecycle.</summary>
    public static string OrderStatus(Order order) => order.Payment?.Status switch
    {
        null => "AwaitingPayment",
        PaymentStatus.Authorized => "Authorized",
        PaymentStatus.Captured => "Fulfilled",
        PaymentStatus.PartiallyRefunded => "PartiallyRefunded",
        PaymentStatus.Refunded => "Refunded",
        PaymentStatus.Voided => "Cancelled",
        _ => "Unknown"
    };
}
