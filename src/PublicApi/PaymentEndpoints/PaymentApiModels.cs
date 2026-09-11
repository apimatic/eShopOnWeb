using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

// ---------- Request bodies ----------

public class PlaceOrderRequestBody
{
    public List<OrderLineDto> Items { get; set; } = new();
    public ShippingAddressDto? ShipToAddress { get; set; }
}

public class OrderLineDto
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

public class ShippingAddressDto
{
    public string? Street { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? Country { get; set; }
    public string? ZipCode { get; set; }
}

public class PayOrderRequestBody
{
    /// <summary>Raw card for a one-off payment. Provide this OR <see cref="SavedPaymentMethodId"/>.</summary>
    public CardDto? Card { get; set; }

    /// <summary>Id of one of the shopper's saved cards to pay with instead of a raw card.</summary>
    public int? SavedPaymentMethodId { get; set; }
}

public class CardDto
{
    public string CardNumber { get; set; } = string.Empty;
    public string Expiry { get; set; } = string.Empty;   // YYYY-MM
    public string SecurityCode { get; set; } = string.Empty;
    public string? CardholderName { get; set; }
    public string? BillingCountryCode { get; set; }
    public string? BillingAddressLine1 { get; set; }
    public string? BillingAddressLine2 { get; set; }
    public string? BillingAdminArea1 { get; set; }   // state/province
    public string? BillingAdminArea2 { get; set; }   // city
    public string? BillingPostalCode { get; set; }

    public CardDetails ToCardDetails() => new(
        CardNumber, Expiry, SecurityCode, CardholderName,
        BillingCountryCode, BillingAddressLine1, BillingAddressLine2,
        BillingAdminArea1, BillingAdminArea2, BillingPostalCode);
}

public class RefundRequestBody
{
    /// <summary>Amount to refund; omit for a full refund of the remaining captured amount.</summary>
    public decimal? Amount { get; set; }

    /// <summary>Caller-supplied idempotency key; repeating under the same key must not refund twice.</summary>
    public string IdempotencyKey { get; set; } = string.Empty;
}

public class SaveCardRequestBody
{
    public CardDto Card { get; set; } = new();
}

// ---------- Response DTOs ----------

public class PlaceOrderResponse
{
    public int OrderId { get; set; }
    public decimal Total { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
}

public class PaymentView
{
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public string Currency { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string? PayPalOrderId { get; set; }
    public string? AuthorizationId { get; set; }
    public DateTimeOffset? AuthorizationExpiresAt { get; set; }
    public string? CaptureId { get; set; }
    public decimal? CapturedAmount { get; set; }
    public decimal? PayPalFee { get; set; }
    public decimal? NetAmount { get; set; }
    public decimal TotalRefunded { get; set; }
    public decimal RefundableRemaining { get; set; }
    public List<RefundView> Refunds { get; set; } = new();

    public static PaymentView From(OrderPayment p) => new()
    {
        OrderId = p.OrderId,
        Status = p.Status.ToString(),
        Currency = p.CurrencyCode,
        Amount = p.Amount,
        PayPalOrderId = p.PayPalOrderId,
        AuthorizationId = p.AuthorizationId,
        AuthorizationExpiresAt = p.AuthorizationExpiresAt,
        CaptureId = p.CaptureId,
        CapturedAmount = p.CapturedAmount,
        PayPalFee = p.PayPalFee,
        NetAmount = p.NetAmount,
        TotalRefunded = p.TotalRefunded(),
        RefundableRemaining = p.RefundableRemaining(),
        Refunds = p.Refunds.Select(RefundView.From).ToList()
    };
}

public class RefundView
{
    public string RefundId { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Status { get; set; } = string.Empty;

    public static RefundView From(PaymentRefund r) => new()
    {
        RefundId = r.PayPalRefundId,
        Amount = r.Amount,
        Status = r.Status
    };
}

public class RefundResponse
{
    public string RefundId { get; set; } = string.Empty;
    public int OrderId { get; set; }
    public decimal Amount { get; set; }
    public string Status { get; set; } = string.Empty;
    public decimal TotalRefunded { get; set; }
    public decimal RefundableRemaining { get; set; }
}

public class MyOrderView
{
    public int OrderId { get; set; }
    public DateTimeOffset OrderDate { get; set; }
    public decimal Total { get; set; }
    public PaymentView? Payment { get; set; }
}

public class SavedCardView
{
    public int PaymentMethodId { get; set; }
    public string Brand { get; set; } = string.Empty;
    public string LastDigits { get; set; } = string.Empty;
    public string? Expiry { get; set; }
    public string? CardholderName { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public static SavedCardView From(SavedPaymentMethod m) => new()
    {
        PaymentMethodId = m.Id,
        Brand = m.Brand,
        LastDigits = m.LastDigits,
        Expiry = m.Expiry,
        CardholderName = m.CardholderName,
        CreatedAt = m.CreatedAt
    };
}

public class SaveCardResponse
{
    public int PaymentMethodId { get; set; }
    public string Brand { get; set; } = string.Empty;
    public string LastDigits { get; set; } = string.Empty;
    public string? Expiry { get; set; }
    public string? CardholderName { get; set; }
}
