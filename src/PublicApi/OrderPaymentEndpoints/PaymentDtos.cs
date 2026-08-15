using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Models.PayPal;

namespace Microsoft.eShopWeb.PublicApi.OrderPaymentEndpoints;

/// <summary>Raw card details accepted for one-off payments and for saving a card. Never persisted.</summary>
public class CardDto
{
    public string Number { get; set; } = string.Empty;

    /// <summary>Expiry in PayPal's YYYY-MM form, e.g. "2027-02".</summary>
    public string Expiry { get; set; } = string.Empty;

    public string? SecurityCode { get; set; }
    public string? Name { get; set; }
    public string? BillingAddressLine1 { get; set; }
    public string? BillingAddressLine2 { get; set; }
    public string? BillingCity { get; set; }
    public string? BillingState { get; set; }
    public string? BillingPostalCode { get; set; }
    public string? BillingCountryCode { get; set; }

    public PayPalCardDetails ToCardDetails() => new()
    {
        Number = Number,
        Expiry = Expiry,
        SecurityCode = SecurityCode,
        Name = Name,
        BillingAddressLine1 = BillingAddressLine1,
        BillingAddressLine2 = BillingAddressLine2,
        BillingCity = BillingCity,
        BillingState = BillingState,
        BillingPostalCode = BillingPostalCode,
        BillingCountryCode = BillingCountryCode
    };
}

public class AddressDto
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

public class OrderLineSummaryDto
{
    public int CatalogItemId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public int Units { get; set; }
}

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
    public string PayPalOrderId { get; set; } = string.Empty;
    public string Currency { get; set; } = string.Empty;
    public decimal AuthorizedAmount { get; set; }
    public string? Instrument { get; set; }

    public string? AuthorizationId { get; set; }
    public string? AuthorizationStatus { get; set; }

    public string? CaptureId { get; set; }
    public string? CaptureStatus { get; set; }
    public decimal? CapturedAmount { get; set; }
    public decimal? PayPalFee { get; set; }
    public decimal? NetProceeds { get; set; }

    public decimal TotalRefunded { get; set; }
    public decimal RefundableRemaining { get; set; }
    public List<RefundDto> Refunds { get; set; } = new();
}

public class OrderSummaryDto
{
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTimeOffset OrderDate { get; set; }
    public decimal Total { get; set; }
    public string Currency { get; set; } = string.Empty;
    public List<OrderLineSummaryDto> Items { get; set; } = new();
    public PaymentStateDto? Payment { get; set; }
}

/// <summary>Maps order aggregate state to the API's payment-aware view.</summary>
public static class PaymentMappings
{
    public static OrderSummaryDto ToSummary(Order order, string fallbackCurrency)
    {
        var payment = order.Payment;
        return new OrderSummaryDto
        {
            OrderId = order.Id,
            Status = order.Status.ToString(),
            OrderDate = order.OrderDate,
            Total = order.Total(),
            Currency = payment?.CurrencyCode ?? fallbackCurrency,
            Items = order.OrderItems.Select(i => new OrderLineSummaryDto
            {
                CatalogItemId = i.ItemOrdered.CatalogItemId,
                ProductName = i.ItemOrdered.ProductName,
                UnitPrice = i.UnitPrice,
                Units = i.Units
            }).ToList(),
            Payment = payment is null ? null : ToPaymentState(payment)
        };
    }

    private static PaymentStateDto ToPaymentState(Payment payment) => new()
    {
        PayPalOrderId = payment.PayPalOrderId,
        Currency = payment.CurrencyCode,
        AuthorizedAmount = payment.AuthorizedAmount,
        Instrument = payment.InstrumentDescription,
        AuthorizationId = payment.AuthorizationId,
        AuthorizationStatus = payment.AuthorizationStatus,
        CaptureId = payment.CaptureId,
        CaptureStatus = payment.CaptureStatus,
        CapturedAmount = payment.CapturedGrossAmount,
        PayPalFee = payment.PayPalFee,
        NetProceeds = payment.NetAmount,
        TotalRefunded = payment.TotalRefunded(),
        RefundableRemaining = payment.RefundableRemaining(),
        Refunds = payment.Refunds
            .OrderBy(r => r.CreatedAt)
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
