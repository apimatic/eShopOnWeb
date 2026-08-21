using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.PayPal;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>Raw card details supplied by a caller. Never persisted or logged by this app.</summary>
public class CardDto
{
    public string Number { get; set; } = string.Empty;
    public int ExpiryMonth { get; set; }
    public int ExpiryYear { get; set; }
    public string SecurityCode { get; set; } = string.Empty;
    public string? CardholderName { get; set; }

    public CardPaymentDetails ToCardPaymentDetails() =>
        new(Number, ExpiryMonth, ExpiryYear, SecurityCode, CardholderName);
}

/// <summary>Shipping address for a placed order.</summary>
public class ShippingAddressDto
{
    public string Street { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string Country { get; set; } = string.Empty;
    public string ZipCode { get; set; } = string.Empty;
}

/// <summary>One line of a placed order.</summary>
public class OrderLineDto
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

/// <summary>A single refund, safe to return to the caller.</summary>
public class RefundDto
{
    public int Id { get; set; }
    public string? PayPalRefundId { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string? Status { get; set; }
    public DateTimeOffset CreatedDate { get; set; }

    public static RefundDto From(PaymentRefund refund) => new()
    {
        Id = refund.Id,
        PayPalRefundId = refund.PayPalRefundId,
        IdempotencyKey = refund.IdempotencyKey,
        Amount = refund.Amount,
        Status = refund.Status,
        CreatedDate = refund.CreatedDate
    };
}

/// <summary>The payment/fulfilment state of an order, including what PayPal reported at capture.</summary>
public class PaymentStateDto
{
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;

    public string? PayPalOrderId { get; set; }
    public string? AuthorizationId { get; set; }
    public string? AuthorizationStatus { get; set; }
    public DateTimeOffset? AuthorizationExpiresAt { get; set; }

    public string? CaptureId { get; set; }
    public string? CaptureStatus { get; set; }
    public decimal? CapturedGrossAmount { get; set; }
    public decimal? PayPalFee { get; set; }
    public decimal? NetProceeds { get; set; }

    public decimal TotalRefunded { get; set; }
    public decimal RefundableRemaining { get; set; }
    public int? SavedPaymentMethodId { get; set; }
    public List<RefundDto> Refunds { get; set; } = new();

    public static PaymentStateDto From(Payment payment) => new()
    {
        OrderId = payment.OrderId,
        Status = payment.Status.ToString(),
        Amount = payment.Amount,
        Currency = payment.CurrencyCode,
        PayPalOrderId = payment.PayPalOrderId,
        AuthorizationId = payment.AuthorizationId,
        AuthorizationStatus = payment.AuthorizationStatus,
        AuthorizationExpiresAt = payment.AuthorizationExpiresAt,
        CaptureId = payment.CaptureId,
        CaptureStatus = payment.CaptureStatus,
        CapturedGrossAmount = payment.CapturedGrossAmount,
        PayPalFee = payment.PayPalFeeAmount,
        NetProceeds = payment.NetAmount,
        TotalRefunded = payment.TotalRefunded(),
        RefundableRemaining = payment.RefundableRemaining(),
        SavedPaymentMethodId = payment.SavedPaymentMethodId,
        Refunds = payment.Refunds.Select(RefundDto.From).ToList()
    };
}

/// <summary>Extracts the authenticated caller's identity from the JWT.</summary>
public static class CallerIdentity
{
    /// <summary>The buyer id (the token's name claim), matching Order.BuyerId. Throws if absent.</summary>
    public static string BuyerId(ClaimsPrincipal user)
    {
        var name = user?.Identity?.Name;
        if (string.IsNullOrEmpty(name))
        {
            throw new ForbiddenAccessException("The caller's identity could not be determined from the token.");
        }
        return name;
    }
}
