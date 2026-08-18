using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>Optional billing address for a card. Only CountryCode is required by PayPal when supplied.</summary>
public class BillingAddressDto
{
    public string CountryCode { get; set; } = string.Empty;
    public string? AddressLine1 { get; set; }
    public string? AddressLine2 { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? PostalCode { get; set; }
}

/// <summary>Raw card details supplied by the caller. Never stored or logged by this application.</summary>
public class CardDto
{
    public string CardNumber { get; set; } = string.Empty;
    public int ExpiryMonth { get; set; }
    public int ExpiryYear { get; set; }
    public string SecurityCode { get; set; } = string.Empty;
    public string? CardholderName { get; set; }
    public BillingAddressDto? BillingAddress { get; set; }

    public CardDetails ToCardDetails()
    {
        var number = new string((CardNumber ?? string.Empty).Where(char.IsDigit).ToArray());
        var expiry = $"{ExpiryYear:D4}-{ExpiryMonth:D2}";
        var billing = BillingAddress is null
            ? null
            : new CardBillingAddress(BillingAddress.CountryCode, BillingAddress.AddressLine1,
                BillingAddress.AddressLine2, BillingAddress.City, BillingAddress.State, BillingAddress.PostalCode);
        return new CardDetails(number, expiry, SecurityCode, CardholderName, billing);
    }
}

/// <summary>A refund line in a payment view.</summary>
public class RefundDto
{
    public string RefundId { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Status { get; set; } = string.Empty;
}

/// <summary>The payment/fulfilment state of an order, safe to return to a shopper.</summary>
public class PaymentDto
{
    public string Status { get; set; } = string.Empty;
    public string Currency { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string? AuthorizationId { get; set; }
    public string? AuthorizationStatus { get; set; }
    public DateTimeOffset? AuthorizationExpiresAt { get; set; }
    public string? CaptureId { get; set; }
    public string? CaptureStatus { get; set; }
    public decimal? CapturedAmount { get; set; }
    public decimal? PayPalFee { get; set; }
    public decimal? NetAmount { get; set; }
    public decimal RefundedTotal { get; set; }
    public decimal RefundableRemaining { get; set; }
    public List<RefundDto> Refunds { get; set; } = new();
}

/// <summary>An order with its payment state, for GET /api/my-orders.</summary>
public class OrderSummaryDto
{
    public int OrderId { get; set; }
    public DateTimeOffset OrderDate { get; set; }
    public decimal Total { get; set; }
    public PaymentDto? Payment { get; set; }
}

public static class PaymentMapping
{
    public static PaymentDto ToDto(OrderPayment payment) => new()
    {
        Status = payment.Status.ToString(),
        Currency = payment.CurrencyCode,
        Amount = payment.Amount,
        AuthorizationId = payment.AuthorizationId,
        AuthorizationStatus = payment.AuthorizationStatus,
        AuthorizationExpiresAt = payment.AuthorizationExpiresAt,
        CaptureId = payment.CaptureId,
        CaptureStatus = payment.CaptureStatus,
        CapturedAmount = payment.CapturedAmount,
        PayPalFee = payment.PayPalFee,
        NetAmount = payment.NetAmount,
        RefundedTotal = payment.TotalRefunded(),
        RefundableRemaining = payment.RefundableRemaining(),
        Refunds = payment.Refunds
            .Select(r => new RefundDto { RefundId = r.RefundId, Amount = r.Amount, Status = r.Status })
            .ToList()
    };

    public static OrderSummaryDto ToSummary(Order order) => new()
    {
        OrderId = order.Id,
        OrderDate = order.OrderDate,
        Total = order.Total(),
        Payment = order.Payment is null ? null : ToDto(order.Payment)
    };
}
