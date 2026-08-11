using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.Payments;

namespace Microsoft.eShopWeb.PublicApi.Payments;

/// <summary>Card details supplied by the caller for a one-off payment or to save a card. Never stored or logged.</summary>
public class CardRequest
{
    /// <summary>Primary account number.</summary>
    public string Number { get; set; } = default!;

    /// <summary>Expiry in ISO-8601 year-month, e.g. <c>2027-01</c>.</summary>
    public string Expiry { get; set; } = default!;

    /// <summary>Card security code (CVV).</summary>
    public string SecurityCode { get; set; } = default!;

    public string? Name { get; set; }

    public BillingAddressRequest? BillingAddress { get; set; }

    public CardDetails ToCardDetails()
    {
        var address = BillingAddress?.ToCardBillingAddress() ?? BillingAddressRequest.Default();
        return new CardDetails(
            Number: (Number ?? string.Empty).Replace(" ", string.Empty),
            ExpiryYearMonth: Expiry,
            SecurityCode: SecurityCode,
            CardholderName: string.IsNullOrWhiteSpace(Name) ? "eShop Shopper" : Name,
            BillingAddress: address);
    }
}

public class BillingAddressRequest
{
    public string? AddressLine1 { get; set; }
    public string? AddressLine2 { get; set; }
    /// <summary>City (PayPal admin_area_2).</summary>
    public string? City { get; set; }
    /// <summary>State / province (PayPal admin_area_1).</summary>
    public string? State { get; set; }
    public string? PostalCode { get; set; }
    /// <summary>Two-letter country code, e.g. <c>US</c>.</summary>
    public string? CountryCode { get; set; }

    public CardBillingAddress ToCardBillingAddress() =>
        new(AddressLine1, AddressLine2, City, State, PostalCode, CountryCode);

    /// <summary>A sensible default billing address so a card can be processed without the caller supplying one.</summary>
    public static CardBillingAddress Default() =>
        new("123 Main St", null, "San Jose", "CA", "95131", "US");
}

/// <summary>A refund as returned in a payment view.</summary>
public class RefundView
{
    public string RefundId { get; set; } = default!;
    public decimal Amount { get; set; }
    public string Status { get; set; } = default!;
    public DateTimeOffset CreatedAt { get; set; }

    public static RefundView From(PaymentRefund r) => new()
    {
        RefundId = r.PayPalRefundId,
        Amount = r.Amount,
        Status = r.Status,
        CreatedAt = r.CreatedAt
    };
}

/// <summary>The full payment state for an order, including everything PayPal owns.</summary>
public class PaymentView
{
    public int OrderId { get; set; }
    public string Status { get; set; } = default!;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = default!;

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
    public decimal RefundableRemaining { get; set; }
    public List<RefundView> Refunds { get; set; } = new();

    public static PaymentView From(Payment p) => new()
    {
        OrderId = p.OrderId,
        Status = p.Status.ToString(),
        Amount = p.Amount,
        Currency = p.CurrencyCode,
        PayPalOrderId = p.PayPalOrderId,
        AuthorizationId = p.AuthorizationId,
        AuthorizationStatus = p.AuthorizationStatus,
        AuthorizationExpiresAt = p.AuthorizationExpiresAt,
        CaptureId = p.CaptureId,
        CaptureStatus = p.CaptureStatus,
        CapturedAmount = p.CapturedAmount,
        PayPalFee = p.PayPalFee,
        NetAmount = p.NetAmount,
        TotalRefunded = p.TotalRefunded,
        RefundableRemaining = p.RefundableRemaining,
        Refunds = p.Refunds.Select(RefundView.From).ToList()
    };
}

/// <summary>A saved card, described safely (never full card details).</summary>
public class PaymentMethodView
{
    public int PaymentMethodId { get; set; }
    public string CardBrand { get; set; } = default!;
    public string LastDigits { get; set; } = default!;
    public string? Expiry { get; set; }
    public string? CardholderName { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public static PaymentMethodView From(SavedPaymentMethod m) => new()
    {
        PaymentMethodId = m.Id,
        CardBrand = m.CardBrand,
        LastDigits = m.LastDigits,
        Expiry = m.ExpiryYearMonth,
        CardholderName = m.CardholderName,
        CreatedAt = m.CreatedAt
    };
}
