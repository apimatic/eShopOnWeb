using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

// ---- Requests --------------------------------------------------------------------------------

/// <summary>Card details for a one-off payment or to vault. Never stored or logged by this app.</summary>
public class CardRequestDto
{
    public string Number { get; set; } = string.Empty;
    public string Expiry { get; set; } = string.Empty; // YYYY-MM
    public string? SecurityCode { get; set; }
    public string? CardHolderName { get; set; }
    public string? BillingStreet { get; set; }
    public string? BillingCity { get; set; }
    public string? BillingState { get; set; }
    public string? BillingCountryCode { get; set; }
    public string? BillingPostalCode { get; set; }

    public CardDetails ToCardDetails() => new()
    {
        Number = Number,
        Expiry = Expiry,
        SecurityCode = SecurityCode,
        CardHolderName = CardHolderName,
        BillingStreet = BillingStreet,
        BillingCity = BillingCity,
        BillingState = BillingState,
        BillingCountryCode = BillingCountryCode,
        BillingPostalCode = BillingPostalCode
    };
}

public class OrderLineDto
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

public class ShipToAddressDto
{
    public string? Street { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? Country { get; set; }
    public string? ZipCode { get; set; }
}

public class PlaceOrderRequest
{
    public List<OrderLineDto> Items { get; set; } = new();
    public ShipToAddressDto? ShipToAddress { get; set; }
}

public class PayOrderRequest
{
    /// <summary>One-off card details, OR set <see cref="PaymentMethodId"/> to pay with a saved card.</summary>
    public CardRequestDto? Card { get; set; }
    public int? PaymentMethodId { get; set; }
}

public class RefundOrderRequest
{
    /// <summary>Amount to refund; omit for a full refund of the remaining balance.</summary>
    public decimal? Amount { get; set; }
    /// <summary>Caller-supplied idempotency key — repeating it never refunds twice.</summary>
    public string IdempotencyKey { get; set; } = string.Empty;
}

public class SavePaymentMethodRequest
{
    public CardRequestDto Card { get; set; } = new();
}

// ---- Responses -------------------------------------------------------------------------------

public class PlaceOrderResponse
{
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
}

public class RefundResponse
{
    public string RefundId { get; set; } = string.Empty;
    public int OrderId { get; set; }
    public decimal Amount { get; set; }
    public string Status { get; set; } = string.Empty;
    public decimal RefundableRemaining { get; set; }
    public string PaymentStatus { get; set; } = string.Empty;
}

public class SavePaymentMethodResponse
{
    public int PaymentMethodId { get; set; }
    public string? Brand { get; set; }
    public string? LastDigits { get; set; }
    public string? Expiry { get; set; }
    public string? CardHolderName { get; set; }
}

public class PaymentMethodDto
{
    public int PaymentMethodId { get; set; }
    public string? Brand { get; set; }
    public string? LastDigits { get; set; }
    public string? Expiry { get; set; }
    public string? CardHolderName { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public static PaymentMethodDto From(SavedPaymentMethod m) => new()
    {
        PaymentMethodId = m.Id,
        Brand = m.Brand,
        LastDigits = m.LastDigits,
        Expiry = m.Expiry,
        CardHolderName = m.CardHolderName,
        CreatedAt = m.CreatedAt
    };
}

public class RefundDto
{
    public string RefundId { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>The payment/fulfilment state of an order, safe to return to the shopper.</summary>
public class OrderPaymentDto
{
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string InvoiceId { get; set; } = string.Empty;

    public string? PayPalOrderId { get; set; }
    public string? AuthorizationId { get; set; }
    public string? AuthorizationStatus { get; set; }
    public DateTimeOffset? AuthorizationExpiresAt { get; set; }

    public string? CaptureId { get; set; }
    public string? CaptureStatus { get; set; }
    public decimal? CapturedAmount { get; set; }
    public decimal? PayPalFee { get; set; }
    public decimal? NetAmount { get; set; }

    public string? CardBrand { get; set; }
    public string? CardLastDigits { get; set; }

    public decimal TotalRefunded { get; set; }
    public decimal RefundableRemaining { get; set; }
    public string? FailureReason { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public List<RefundDto> Refunds { get; set; } = new();

    public static OrderPaymentDto From(OrderPayment p) => new()
    {
        OrderId = p.OrderId,
        Status = p.Status.ToString(),
        Amount = p.Amount,
        Currency = p.Currency,
        InvoiceId = p.InvoiceId,
        PayPalOrderId = p.PayPalOrderId,
        AuthorizationId = p.AuthorizationId,
        AuthorizationStatus = p.AuthorizationStatus,
        AuthorizationExpiresAt = p.AuthorizationExpiresAt,
        CaptureId = p.CaptureId,
        CaptureStatus = p.CaptureStatus,
        CapturedAmount = p.CapturedAmount,
        PayPalFee = p.PayPalFee,
        NetAmount = p.NetAmount,
        CardBrand = p.CardBrand,
        CardLastDigits = p.CardLastDigits,
        TotalRefunded = p.TotalRefunded,
        RefundableRemaining = p.RefundableRemaining,
        FailureReason = p.FailureReason,
        CreatedAt = p.CreatedAt,
        UpdatedAt = p.UpdatedAt,
        Refunds = p.Refunds
            .OrderBy(r => r.CreatedAt)
            .Select(r => new RefundDto { RefundId = r.RefundId, Amount = r.Amount, Status = r.Status, CreatedAt = r.CreatedAt })
            .ToList()
    };
}
