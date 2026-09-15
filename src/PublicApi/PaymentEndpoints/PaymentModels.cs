using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.PayPal;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

// ---------------------------------------------------------------------------
// Inbound DTOs
// ---------------------------------------------------------------------------

/// <summary>Raw card details supplied by the shopper. Never stored or logged by this app.</summary>
public class CardDto
{
    public string Number { get; set; } = string.Empty;
    /// <summary>Card expiry in YYYY-MM form, e.g. 2030-01.</summary>
    public string Expiry { get; set; } = string.Empty;
    public string SecurityCode { get; set; } = string.Empty;
    public string? Name { get; set; }
    public BillingAddressDto? BillingAddress { get; set; }

    public CardDetails ToCardDetails() => new(
        Number: Number,
        Expiry: Expiry,
        SecurityCode: SecurityCode,
        Name: Name,
        BillingAddress: BillingAddress?.ToDomain());
}

public class BillingAddressDto
{
    public string? AddressLine1 { get; set; }
    public string? AddressLine2 { get; set; }
    /// <summary>City / town.</summary>
    public string? AdminArea2 { get; set; }
    /// <summary>State / province.</summary>
    public string? AdminArea1 { get; set; }
    public string? PostalCode { get; set; }
    /// <summary>Two-letter ISO country code (required by PayPal when a billing address is supplied).</summary>
    public string CountryCode { get; set; } = "US";

    public CardBillingAddress ToDomain() => new(
        AddressLine1, AddressLine2, AdminArea2, AdminArea1, PostalCode, CountryCode);
}

/// <summary>Optional shipping address for a placed order.</summary>
public class ShipAddressDto
{
    public string Street { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string Country { get; set; } = string.Empty;
    public string ZipCode { get; set; } = string.Empty;

    public Address ToDomain() => new(Street, City, State, Country, ZipCode);
}

// ---------------------------------------------------------------------------
// Outbound DTOs
// ---------------------------------------------------------------------------

public class RefundDto
{
    public string RefundId { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }

    public static RefundDto From(Refund r) => new()
    {
        RefundId = r.PayPalRefundId,
        Amount = r.Amount,
        Currency = r.Currency,
        Status = r.Status,
        CreatedAt = r.CreatedAt
    };
}

/// <summary>The payment state for an order, mirroring what PayPal owns. Never contains card data.</summary>
public class PaymentDto
{
    public int OrderId { get; set; }
    public string OrderStatus { get; set; } = string.Empty;
    public string PaymentState { get; set; } = string.Empty;
    public string Currency { get; set; } = string.Empty;
    public decimal Amount { get; set; }

    public string? PayPalOrderId { get; set; }
    public string? AuthorizationId { get; set; }
    public string? AuthorizationStatus { get; set; }
    public DateTimeOffset? AuthorizationExpiresAt { get; set; }

    public string? CaptureId { get; set; }
    public string? CaptureStatus { get; set; }
    public decimal? CapturedAmount { get; set; }
    public decimal? PayPalFee { get; set; }
    public decimal? NetAmount { get; set; }

    public decimal RefundedAmount { get; set; }
    public decimal RefundableAmount { get; set; }
    public List<RefundDto> Refunds { get; set; } = new();

    public static PaymentDto? From(OrderStatus orderStatus, Payment? payment)
    {
        if (payment is null)
        {
            return null;
        }
        return new PaymentDto
        {
            OrderId = payment.OrderId,
            OrderStatus = orderStatus.ToString(),
            PaymentState = payment.State.ToString(),
            Currency = payment.Currency,
            Amount = payment.Amount,
            PayPalOrderId = payment.PayPalOrderId,
            AuthorizationId = payment.AuthorizationId,
            AuthorizationStatus = payment.AuthorizationStatus,
            AuthorizationExpiresAt = payment.AuthorizationExpiresAt,
            CaptureId = payment.CaptureId,
            CaptureStatus = payment.CaptureStatus,
            CapturedAmount = payment.CapturedAmount,
            PayPalFee = payment.PayPalFee,
            NetAmount = payment.NetAmount,
            RefundedAmount = payment.RefundedAmount(),
            RefundableAmount = payment.RefundableAmount(),
            Refunds = payment.Refunds.Select(RefundDto.From).ToList()
        };
    }
}

public class OrderLineDto
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
    public string Status { get; set; } = string.Empty;
    public decimal Total { get; set; }
    public List<OrderLineDto> Items { get; set; } = new();
    public PaymentDto? Payment { get; set; }

    public static OrderSummaryDto From(OrderWithPayment owp)
    {
        var order = owp.Order;
        return new OrderSummaryDto
        {
            OrderId = order.Id,
            OrderDate = order.OrderDate,
            Status = order.Status.ToString(),
            Total = order.Total(),
            Items = order.OrderItems.Select(i => new OrderLineDto
            {
                CatalogItemId = i.ItemOrdered.CatalogItemId,
                ProductName = i.ItemOrdered.ProductName,
                UnitPrice = i.UnitPrice,
                Units = i.Units
            }).ToList(),
            Payment = PaymentDto.From(order.Status, owp.Payment)
        };
    }
}

public class PaymentMethodDto
{
    public int PaymentMethodId { get; set; }
    public string? Alias { get; set; }
    public string? Brand { get; set; }
    public string? Last4 { get; set; }
    public string? Expiry { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public static PaymentMethodDto From(PaymentMethod pm) => new()
    {
        PaymentMethodId = pm.Id,
        Alias = pm.Alias,
        Brand = pm.Brand,
        Last4 = pm.Last4,
        Expiry = pm.Expiry,
        CreatedAt = pm.CreatedAt
    };
}
