using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.PaymentGateway;

namespace Microsoft.eShopWeb.PublicApi.Payments;

/// <summary>Card details supplied by a caller. Never stored or logged by this app.</summary>
public class CardDto
{
    public string Number { get; set; } = string.Empty;
    public string Expiry { get; set; } = string.Empty; // "YYYY-MM"
    public string SecurityCode { get; set; } = string.Empty;
    public string? CardholderName { get; set; }
    public BillingAddressDto? BillingAddress { get; set; }

    public CardDetails ToDomain() =>
        new(Number, Expiry, SecurityCode, CardholderName, BillingAddress?.ToDomain());
}

public class BillingAddressDto
{
    public string? AddressLine1 { get; set; }
    public string? AddressLine2 { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? PostalCode { get; set; }
    public string? CountryCode { get; set; }

    public BillingAddress ToDomain() =>
        new(AddressLine1, AddressLine2, City, State, PostalCode, string.IsNullOrWhiteSpace(CountryCode) ? "US" : CountryCode!);
}

/// <summary>An order's payment state (safe to return to the shopper).</summary>
public class OrderPaymentDto
{
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string MerchantReference { get; set; } = string.Empty;
    public string? PayPalOrderId { get; set; }
    public string? AuthorizationId { get; set; }
    public string? AuthorizationStatus { get; set; }
    public DateTimeOffset? AuthorizationExpiresAt { get; set; }
    public string? CaptureId { get; set; }
    public string? CaptureStatus { get; set; }
    public decimal? CapturedGross { get; set; }
    public decimal? PayPalFee { get; set; }
    public decimal? NetAmount { get; set; }
    public decimal TotalRefunded { get; set; }
    public decimal? RefundableRemaining { get; set; }
    public List<RefundDto> Refunds { get; set; } = new();

    public static OrderPaymentDto From(OrderPayment p) => new()
    {
        OrderId = p.OrderId,
        Status = p.Status.ToString(),
        Amount = p.Amount,
        Currency = p.CurrencyCode,
        MerchantReference = p.MerchantReference,
        PayPalOrderId = p.PayPalOrderId,
        AuthorizationId = p.AuthorizationId,
        AuthorizationStatus = p.AuthorizationStatus,
        AuthorizationExpiresAt = p.AuthorizationExpiresAt,
        CaptureId = p.CaptureId,
        CaptureStatus = p.CaptureStatus,
        CapturedGross = p.CapturedGross,
        PayPalFee = p.PayPalFee,
        NetAmount = p.NetAmount,
        TotalRefunded = p.TotalRefunded(),
        RefundableRemaining = p.IsCaptured ? p.RefundableRemaining() : null,
        Refunds = p.Refunds.Select(RefundDto.From).ToList()
    };
}

public class RefundDto
{
    public int Id { get; set; }
    public string PayPalRefundId { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTimeOffset CreatedDate { get; set; }

    public static RefundDto From(PaymentRefund r) => new()
    {
        Id = r.Id,
        PayPalRefundId = r.PayPalRefundId,
        Amount = r.Amount,
        Status = r.Status,
        CreatedDate = r.CreatedDate
    };
}

/// <summary>A saved card described safely — never full card details.</summary>
public class PaymentMethodDto
{
    public int Id { get; set; }
    public string? Brand { get; set; }
    public string? Last4 { get; set; }
    public string? Expiry { get; set; }
    public string? Alias { get; set; }
    public DateTimeOffset CreatedDate { get; set; }

    public static PaymentMethodDto From(PaymentMethod pm) => new()
    {
        Id = pm.Id,
        Brand = pm.Brand,
        Last4 = pm.Last4,
        Expiry = pm.Expiry,
        Alias = pm.Alias,
        CreatedDate = pm.CreatedDate
    };
}
