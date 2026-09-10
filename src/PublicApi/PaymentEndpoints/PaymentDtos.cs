using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

// ----- Shared card input (one-off payment or a card to be saved) -----

public class CardDto
{
    public string Number { get; set; } = string.Empty;
    /// <summary>Card expiry. Accepts YYYY-MM (preferred), MM/YY or MM/YYYY.</summary>
    public string Expiry { get; set; } = string.Empty;
    public string? SecurityCode { get; set; }
    public string? Name { get; set; }
    public BillingAddressDto? BillingAddress { get; set; }

    public CardDetails ToCardDetails() => new(
        Number, Expiry, SecurityCode, Name,
        BillingAddress is null
            ? null
            : new CardBillingAddress(
                BillingAddress.CountryCode, BillingAddress.AddressLine1, BillingAddress.AddressLine2,
                BillingAddress.AdminArea1, BillingAddress.AdminArea2, BillingAddress.PostalCode));
}

public class BillingAddressDto
{
    public string CountryCode { get; set; } = string.Empty;
    public string? AddressLine1 { get; set; }
    public string? AddressLine2 { get; set; }
    public string? AdminArea1 { get; set; }
    public string? AdminArea2 { get; set; }
    public string? PostalCode { get; set; }
}

public class ShippingAddressDto
{
    public string? Street { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? Country { get; set; }
    public string? ZipCode { get; set; }
}

// ----- Order / payment output -----

public class OrderSummaryDto
{
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTimeOffset OrderDate { get; set; }
    public decimal Total { get; set; }
    public string? Currency { get; set; }
    public List<OrderLineDto> Items { get; set; } = new();
    public PaymentStateDto? Payment { get; set; }

    public static OrderSummaryDto From(Order order)
    {
        return new OrderSummaryDto
        {
            OrderId = order.Id,
            Status = order.Status.ToString(),
            OrderDate = order.OrderDate,
            Total = order.Total(),
            Currency = order.Payment?.Currency,
            Items = order.OrderItems.Select(i => new OrderLineDto
            {
                CatalogItemId = i.ItemOrdered.CatalogItemId,
                ProductName = i.ItemOrdered.ProductName,
                UnitPrice = i.UnitPrice,
                Units = i.Units
            }).ToList(),
            Payment = order.Payment is null ? null : PaymentStateDto.From(order.Payment)
        };
    }
}

public class OrderLineDto
{
    public int CatalogItemId { get; set; }
    public string? ProductName { get; set; }
    public decimal UnitPrice { get; set; }
    public int Units { get; set; }
}

public class PaymentStateDto
{
    public string ReferenceId { get; set; } = string.Empty;
    public string Currency { get; set; } = string.Empty;
    public string PayPalOrderId { get; set; } = string.Empty;
    public string AuthorizationId { get; set; } = string.Empty;
    public string AuthorizationStatus { get; set; } = string.Empty;
    public decimal AuthorizedAmount { get; set; }
    public DateTimeOffset? AuthorizationExpiresAt { get; set; }
    public string? CaptureId { get; set; }
    public string? CaptureStatus { get; set; }
    public decimal? CapturedAmount { get; set; }
    public decimal? PayPalFee { get; set; }
    public decimal? NetAmount { get; set; }
    public string? CardBrand { get; set; }
    public string? CardLast4 { get; set; }
    public int? SavedPaymentMethodId { get; set; }
    public decimal TotalRefunded { get; set; }
    public decimal RefundableRemaining { get; set; }
    public List<RefundDto> Refunds { get; set; } = new();

    public static PaymentStateDto From(PaymentRecord p) => new()
    {
        ReferenceId = p.ReferenceId,
        Currency = p.Currency,
        PayPalOrderId = p.PayPalOrderId,
        AuthorizationId = p.AuthorizationId,
        AuthorizationStatus = p.AuthorizationStatus,
        AuthorizedAmount = p.AuthorizedAmount,
        AuthorizationExpiresAt = p.AuthorizationExpiresAt,
        CaptureId = p.CaptureId,
        CaptureStatus = p.CaptureStatus,
        CapturedAmount = p.CapturedAmount,
        PayPalFee = p.PayPalFee,
        NetAmount = p.NetAmount,
        CardBrand = p.CardBrand,
        CardLast4 = p.CardLast4,
        SavedPaymentMethodId = p.SavedPaymentMethodId,
        TotalRefunded = p.TotalRefunded,
        RefundableRemaining = p.RefundableRemaining,
        Refunds = p.Refunds.Select(RefundDto.From).ToList()
    };
}

public class RefundDto
{
    public string RefundId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public static RefundDto From(PaymentRefund r) => new()
    {
        RefundId = r.PayPalRefundId,
        Status = r.Status,
        Amount = r.Amount,
        CreatedAt = r.CreatedAt
    };
}

public class SavedCardDto
{
    public int PaymentMethodId { get; set; }
    public string? Brand { get; set; }
    public string? Last4 { get; set; }
    public string? Expiry { get; set; }
    public string? CardholderName { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public static SavedCardDto From(SavedPaymentMethod pm) => new()
    {
        PaymentMethodId = pm.Id,
        Brand = pm.Brand,
        Last4 = pm.Last4,
        Expiry = pm.Expiry,
        CardholderName = pm.CardholderName,
        CreatedAt = pm.CreatedAt
    };
}
