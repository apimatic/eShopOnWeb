using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.PaymentGateway;
using Microsoft.eShopWeb.ApplicationCore.Services;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>Card details for a one-off payment or to be saved. Never stored or logged by this app.</summary>
public class CardDto
{
    public string Number { get; set; } = string.Empty;

    /// <summary>Expiry in YYYY-MM form (PayPal's format).</summary>
    public string Expiry { get; set; } = string.Empty;

    public string SecurityCode { get; set; } = string.Empty;
    public string? CardholderName { get; set; }
    public BillingAddressDto? BillingAddress { get; set; }

    public CardDetails ToCardDetails() => new(
        Number, Expiry, SecurityCode, CardholderName, BillingAddress?.ToBillingAddress());
}

public class BillingAddressDto
{
    public string? Line1 { get; set; }
    public string? Line2 { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? PostalCode { get; set; }
    public string CountryCode { get; set; } = string.Empty;

    public BillingAddress ToBillingAddress() => new(Line1, Line2, City, State, PostalCode, CountryCode);
}

/// <summary>An order together with its payment state, as returned to the caller.</summary>
public class ApiOrderPayment
{
    public int OrderId { get; set; }
    public string BuyerId { get; set; } = string.Empty;
    public decimal Total { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string PaymentStatus { get; set; } = string.Empty;
    public DateTimeOffset OrderDate { get; set; }

    public string? PayPalOrderId { get; set; }
    public string? AuthorizationId { get; set; }
    public string? AuthorizationStatus { get; set; }
    public DateTimeOffset? AuthorizationExpiresAt { get; set; }

    public string? CaptureId { get; set; }
    public decimal? CapturedAmount { get; set; }
    public decimal? PayPalFee { get; set; }
    public decimal? NetAmount { get; set; }

    public decimal TotalRefunded { get; set; }
    public decimal RefundableAmount { get; set; }

    public string? CardBrand { get; set; }
    public string? CardLast4 { get; set; }

    public List<ApiOrderLine> Items { get; set; } = new();
    public List<ApiRefund> Refunds { get; set; } = new();

    public static ApiOrderPayment From(OrderPaymentView v) => new()
    {
        OrderId = v.OrderId,
        BuyerId = v.BuyerId,
        Total = v.Total,
        Currency = v.Currency,
        PaymentStatus = v.PaymentStatus,
        OrderDate = v.OrderDate,
        PayPalOrderId = v.PayPalOrderId,
        AuthorizationId = v.AuthorizationId,
        AuthorizationStatus = v.AuthorizationStatus,
        AuthorizationExpiresAt = v.AuthorizationExpiresAt,
        CaptureId = v.CaptureId,
        CapturedAmount = v.CapturedAmount,
        PayPalFee = v.PayPalFee,
        NetAmount = v.NetAmount,
        TotalRefunded = v.TotalRefunded,
        RefundableAmount = v.RefundableAmount,
        CardBrand = v.CardBrand,
        CardLast4 = v.CardLast4,
        Items = v.Items.Select(i => new ApiOrderLine
        {
            CatalogItemId = i.CatalogItemId,
            ProductName = i.ProductName,
            UnitPrice = i.UnitPrice,
            Units = i.Units
        }).ToList(),
        Refunds = v.Refunds.Select(r => new ApiRefund
        {
            RefundId = r.RefundId,
            PayPalRefundId = r.PayPalRefundId,
            Amount = r.Amount,
            Status = r.Status,
            CreatedAt = r.CreatedAt
        }).ToList()
    };
}

public class ApiOrderLine
{
    public int CatalogItemId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public int Units { get; set; }
}

public class ApiRefund
{
    public int RefundId { get; set; }
    public string PayPalRefundId { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}
