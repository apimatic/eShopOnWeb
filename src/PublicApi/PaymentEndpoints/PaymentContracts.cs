using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.PayPal;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>A postal address as accepted by the API.</summary>
public class AddressDto
{
    public string? Street { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? Country { get; set; }
    public string? ZipCode { get; set; }

    public Address ToOrderAddress() => new(
        Street ?? "N/A", City ?? "N/A", State ?? "N/A", Country ?? "N/A", ZipCode ?? "00000");

    public PayPalAddress ToPayPalAddress() => new(
        CountryCode: string.IsNullOrWhiteSpace(Country) ? "US" : Country!,
        AddressLine1: Street,
        AdminArea1: State,
        AdminArea2: City,
        PostalCode: ZipCode);
}

/// <summary>One catalog line in a placed order.</summary>
public class OrderLineDto
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

/// <summary>Raw card details. Sent to PayPal only; never stored by the application or written to logs.</summary>
public class CardDto
{
    /// <summary>The primary account number.</summary>
    public string Number { get; set; } = string.Empty;

    /// <summary>The expiry in ISO year-month form, e.g. "2030-01".</summary>
    public string Expiry { get; set; } = string.Empty;

    /// <summary>The card security code (CVV).</summary>
    public string SecurityCode { get; set; } = string.Empty;

    public string? Name { get; set; }
    public AddressDto? BillingAddress { get; set; }

    public PayPalCardDetails ToCardDetails() =>
        new(Number, Expiry, SecurityCode, Name, BillingAddress?.ToPayPalAddress());
}

/// <summary>The full payment state for an order, including the PayPal ids and statuses a later request needs.</summary>
public class PaymentDto
{
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string? Card { get; set; }
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
    public List<PaymentRefundDto> Refunds { get; set; } = new();

    public static PaymentDto From(Payment payment) => new()
    {
        OrderId = payment.OrderId,
        Status = payment.Status.ToString(),
        Amount = payment.Amount,
        Currency = payment.CurrencyCode,
        Card = payment.CardDescription,
        PayPalOrderId = payment.PayPalOrderId,
        AuthorizationId = payment.AuthorizationId,
        AuthorizationStatus = payment.AuthorizationStatus,
        AuthorizationExpiresAt = payment.AuthorizationExpiresAt,
        CaptureId = payment.CaptureId,
        CaptureStatus = payment.CaptureStatus,
        CapturedAmount = payment.CapturedAmount,
        PayPalFee = payment.PayPalFee,
        NetAmount = payment.NetAmount,
        TotalRefunded = payment.TotalRefunded(),
        RefundableAmount = payment.RefundableAmount(),
        Refunds = payment.Refunds
            .OrderBy(r => r.CreatedAt)
            .Select(PaymentRefundDto.From)
            .ToList()
    };
}

public class PaymentRefundDto
{
    public string RefundId { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }

    public static PaymentRefundDto From(PaymentRefund refund) => new()
    {
        RefundId = refund.RefundId,
        Amount = refund.Amount,
        Status = refund.Status,
        CreatedAt = refund.CreatedAt
    };
}

/// <summary>A safe description of a saved card for the shopper.</summary>
public class SavedCardDto
{
    public int PaymentMethodId { get; set; }
    public string? Brand { get; set; }
    public string? Last4 { get; set; }
    public string? Expiry { get; set; }
    public string Description { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }

    public static SavedCardDto From(SavedCard card) => new()
    {
        PaymentMethodId = card.Id,
        Brand = card.Brand,
        Last4 = card.Last4,
        Expiry = card.Expiry,
        Description = card.Description,
        CreatedAt = card.CreatedAt
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
    public string PaymentStatus { get; set; } = string.Empty;
    public List<OrderItemDto> Items { get; set; } = new();
    public PaymentDto? Payment { get; set; }

    public static OrderSummaryDto From(Order order, Payment? payment) => new()
    {
        OrderId = order.Id,
        OrderDate = order.OrderDate,
        Total = order.Total(),
        PaymentStatus = payment?.Status.ToString()
            ?? Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate.PaymentStatus.PendingAuthorization.ToString(),
        Items = order.OrderItems.Select(i => new OrderItemDto
        {
            CatalogItemId = i.ItemOrdered.CatalogItemId,
            ProductName = i.ItemOrdered.ProductName,
            UnitPrice = i.UnitPrice,
            Units = i.Units
        }).ToList(),
        Payment = payment is null ? null : PaymentDto.From(payment)
    };
}
