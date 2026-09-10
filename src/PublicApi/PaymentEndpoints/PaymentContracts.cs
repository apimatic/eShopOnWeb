using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

// ---- Requests ---------------------------------------------------------------------------------------

/// <summary>Card details for a one-off payment or to vault. Never persisted or logged by this app.</summary>
public class CardDto
{
    public string Number { get; set; } = string.Empty;
    /// <summary>Expiry in ISO-8601 <c>YYYY-MM</c> form.</summary>
    public string Expiry { get; set; } = string.Empty;
    public string SecurityCode { get; set; } = string.Empty;
    public string? CardholderName { get; set; }

    public string? BillingAddressLine1 { get; set; }
    public string? BillingAddressLine2 { get; set; }
    public string? BillingCity { get; set; }
    public string? BillingState { get; set; }
    public string? BillingPostalCode { get; set; }
    public string? BillingCountryCode { get; set; }

    public CardDetails ToCardDetails() => new()
    {
        Number = Number,
        Expiry = Expiry,
        SecurityCode = SecurityCode,
        CardholderName = CardholderName,
        BillingAddressLine1 = BillingAddressLine1,
        BillingAddressLine2 = BillingAddressLine2,
        BillingCity = BillingCity,
        BillingState = BillingState,
        BillingPostalCode = BillingPostalCode,
        BillingCountryCode = BillingCountryCode,
    };

    public bool HasAnyCardData =>
        !string.IsNullOrWhiteSpace(Number) || !string.IsNullOrWhiteSpace(SecurityCode) || !string.IsNullOrWhiteSpace(Expiry);
}

public class AddressDto
{
    public string Street { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string Country { get; set; } = string.Empty;
    public string ZipCode { get; set; } = string.Empty;

    public Address ToAddress() => new(Street, City, State, Country, ZipCode);
}

public class OrderLineDto
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

public class PlaceOrderRequest
{
    public List<OrderLineDto> Items { get; set; } = new();
    public AddressDto? ShipToAddress { get; set; }
}

public class PayOrderRequest
{
    /// <summary>Set from the route; ignored if present in the body.</summary>
    public int OrderId { get; set; }
    public CardDto? Card { get; set; }
    public int? SavedPaymentMethodId { get; set; }
}

public class RefundOrderRequest
{
    /// <summary>Set from the route; ignored if present in the body.</summary>
    public int OrderId { get; set; }
    public decimal? Amount { get; set; }
    /// <summary>Caller-supplied idempotency key; repeating a request under the same key never refunds twice.</summary>
    public string IdempotencyKey { get; set; } = string.Empty;
}

/// <summary>Route-only reference to an order (operator actions with no body).</summary>
public record OrderReference(int OrderId);

/// <summary>Route-only reference to a saved payment method.</summary>
public record PaymentMethodReference(int PaymentMethodId);

/// <summary>Query parameters for the reconciliation report.</summary>
public record ReconciliationRequest(DateTimeOffset From, DateTimeOffset To);

public class SavePaymentMethodRequest
{
    public CardDto? Card { get; set; }
}

// ---- Responses --------------------------------------------------------------------------------------

public class RefundResponse
{
    public string? RefundId { get; set; }
    public decimal Amount { get; set; }
    public string? Status { get; set; }

    public static RefundResponse From(RefundView r) => new()
    {
        RefundId = r.RefundId,
        Amount = r.Amount,
        Status = r.Status,
    };
}

public class OrderPaymentResponse
{
    public int OrderId { get; set; }
    public DateTimeOffset OrderDate { get; set; }
    public string Status { get; set; } = string.Empty;
    public string Currency { get; set; } = string.Empty;
    public decimal Amount { get; set; }

    public string? PayPalOrderId { get; set; }
    public string? AuthorizationId { get; set; }
    public string? AuthorizationStatus { get; set; }
    public string? AuthorizationExpiresAt { get; set; }
    public string? CaptureId { get; set; }
    public string? CaptureStatus { get; set; }
    public decimal? CapturedAmount { get; set; }
    public decimal? PayPalFee { get; set; }
    public decimal? NetAmount { get; set; }
    public decimal RefundedAmount { get; set; }
    public decimal RefundableRemaining { get; set; }
    public List<RefundResponse> Refunds { get; set; } = new();

    public static OrderPaymentResponse From(OrderPaymentView v) => new()
    {
        OrderId = v.OrderId,
        OrderDate = v.OrderDate,
        Status = v.Status,
        Currency = v.Currency,
        Amount = v.Amount,
        PayPalOrderId = v.PayPalOrderId,
        AuthorizationId = v.AuthorizationId,
        AuthorizationStatus = v.AuthorizationStatus,
        AuthorizationExpiresAt = v.AuthorizationExpiresAt,
        CaptureId = v.CaptureId,
        CaptureStatus = v.CaptureStatus,
        CapturedAmount = v.CapturedAmount,
        PayPalFee = v.PayPalFee,
        NetAmount = v.NetAmount,
        RefundedAmount = v.RefundedAmount,
        RefundableRemaining = v.RefundableRemaining,
        Refunds = v.Refunds.Select(RefundResponse.From).ToList(),
    };
}

public class PlaceOrderResponse
{
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
}

public class RefundOrderResponse
{
    public string? RefundId { get; set; }
    public RefundResponse Refund { get; set; } = new();
    public OrderPaymentResponse Payment { get; set; } = new();
}

public class SavedCardResponse
{
    public int PaymentMethodId { get; set; }
    public string? CardBrand { get; set; }
    public string? LastFourDigits { get; set; }
    public string? Expiry { get; set; }
    public string? CardholderName { get; set; }
    public DateTimeOffset CreatedDate { get; set; }

    public static SavedCardResponse From(SavedCardView v) => new()
    {
        PaymentMethodId = v.PaymentMethodId,
        CardBrand = v.CardBrand,
        LastFourDigits = v.LastFourDigits,
        Expiry = v.Expiry,
        CardholderName = v.CardholderName,
        CreatedDate = v.CreatedDate,
    };
}

public class SavedCardListResponse
{
    public List<SavedCardResponse> PaymentMethods { get; set; } = new();
}

public class MyOrdersResponse
{
    public List<OrderPaymentResponse> Orders { get; set; } = new();
}
