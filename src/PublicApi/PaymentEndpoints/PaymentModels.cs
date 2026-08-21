using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.SavedCardAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>Card details supplied in a request. Never persisted or logged in full by this app.</summary>
public class CardModel
{
    public string Number { get; set; } = string.Empty;
    public string Expiry { get; set; } = string.Empty;
    public string SecurityCode { get; set; } = string.Empty;
    public string? CardholderName { get; set; }
    public string? BillingLine1 { get; set; }
    public string? BillingLine2 { get; set; }
    public string? BillingCity { get; set; }
    public string? BillingState { get; set; }
    public string? BillingPostalCode { get; set; }
    public string? BillingCountryCode { get; set; }

    public CardDetails ToCardDetails() => new(
        Number,
        Expiry,
        SecurityCode,
        CardholderName,
        new CardBillingAddress(BillingLine1, BillingLine2, BillingCity, BillingState, BillingPostalCode, BillingCountryCode));
}

public class RefundDto
{
    public string RefundId { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTimeOffset CreatedDate { get; set; }
}

/// <summary>A shopper's order with its payment state.</summary>
public class OrderPaymentDto
{
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public DateTimeOffset CreatedDate { get; set; }
    public string? PaymentMethod { get; set; }

    public string? PayPalOrderId { get; set; }
    public string? AuthorizationId { get; set; }
    public string? AuthorizationStatus { get; set; }

    public string? CaptureId { get; set; }
    public string? CaptureStatus { get; set; }
    public decimal? CapturedAmount { get; set; }
    public decimal? PayPalFee { get; set; }
    public decimal? NetAmount { get; set; }

    public decimal TotalRefunded { get; set; }
    public decimal RefundableRemaining { get; set; }
    public List<RefundDto> Refunds { get; set; } = new();
}

public class SavedCardDto
{
    public int PaymentMethodId { get; set; }
    public string? Brand { get; set; }
    public string? LastFourDigits { get; set; }
    public string? Expiry { get; set; }
    public DateTimeOffset CreatedDate { get; set; }
}

/// <summary>Maps domain entities to safe API DTOs.</summary>
public static class PaymentMapper
{
    public static OrderPaymentDto ToDto(OrderPayment payment) => new()
    {
        OrderId = payment.OrderId,
        Status = payment.Status.ToString(),
        Amount = payment.Amount,
        Currency = payment.Currency,
        CreatedDate = payment.CreatedDate,
        PaymentMethod = payment.PaymentMethodDescriptor,
        PayPalOrderId = payment.PayPalOrderId,
        AuthorizationId = payment.AuthorizationId,
        AuthorizationStatus = payment.AuthorizationStatus,
        CaptureId = payment.CaptureId,
        CaptureStatus = payment.CaptureStatus,
        CapturedAmount = payment.CapturedAmount,
        PayPalFee = payment.PayPalFee,
        NetAmount = payment.NetAmount,
        TotalRefunded = payment.TotalRefunded(),
        RefundableRemaining = payment.RefundableRemaining(),
        Refunds = payment.Refunds
            .OrderBy(r => r.CreatedDate)
            .Select(r => new RefundDto
            {
                RefundId = r.PayPalRefundId,
                Amount = r.Amount,
                Status = r.Status,
                CreatedDate = r.CreatedDate
            }).ToList()
    };

    public static SavedCardDto ToDto(SavedCard card) => new()
    {
        PaymentMethodId = card.Id,
        Brand = card.CardBrand,
        LastFourDigits = card.LastFourDigits,
        Expiry = card.Expiry,
        CreatedDate = card.CreatedDate
    };
}

/// <summary>Reads the authenticated caller's identity from the token.</summary>
public static class CallerIdentity
{
    public static string BuyerId(ClaimsPrincipal user)
    {
        var name = user.Identity?.Name;
        if (string.IsNullOrEmpty(name))
            throw new PaymentException("The caller's identity could not be determined from the token.", PaymentErrorKind.Forbidden);
        return name;
    }
}
