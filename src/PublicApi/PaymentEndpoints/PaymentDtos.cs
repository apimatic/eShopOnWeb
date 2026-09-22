using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

// ---- Requests ----

public record CardDto
{
    public string? Number { get; init; }
    /// <summary>Expiry in YYYY-MM form.</summary>
    public string? Expiry { get; init; }
    public string? SecurityCode { get; init; }
    public string? CardholderName { get; init; }
    public string? BillingLine1 { get; init; }
    public string? BillingCity { get; init; }
    public string? BillingState { get; init; }
    public string? BillingPostalCode { get; init; }
    /// <summary>ISO-3166-1 alpha-2 country code, e.g. "US".</summary>
    public string? BillingCountryCode { get; init; }
}

public record AddressDto
{
    public string? Street { get; init; }
    public string? City { get; init; }
    public string? State { get; init; }
    public string? Country { get; init; }
    public string? ZipCode { get; init; }
}

public record PlaceOrderLineDto
{
    public int CatalogItemId { get; init; }
    public int Quantity { get; init; }
}

public record PlaceOrderRequest
{
    public List<PlaceOrderLineDto> Items { get; init; } = new();
    public AddressDto? ShipToAddress { get; init; }
}

public record PayOrderRequest
{
    public CardDto? Card { get; init; }
    public int? SavedPaymentMethodId { get; init; }
}

public record RefundRequestDto
{
    public decimal? Amount { get; init; }
    /// <summary>Caller-supplied idempotency key; repeating it must not refund twice.</summary>
    public string? IdempotencyKey { get; init; }
}

public record SavePaymentMethodRequest
{
    public CardDto? Card { get; init; }
}

// ---- Responses ----

public record PlaceOrderResponse
{
    public int OrderId { get; init; }
    public string State { get; init; } = string.Empty;
    public decimal Total { get; init; }
    public string Currency { get; init; } = string.Empty;
}

public record RefundResponseDto
{
    public string RefundId { get; init; } = string.Empty;
    public int OrderId { get; init; }
    public decimal Amount { get; init; }
    public string Status { get; init; } = string.Empty;
    public string State { get; init; } = string.Empty;
}

public record PaymentMethodDto
{
    public int PaymentMethodId { get; init; }
    public string? Brand { get; init; }
    public string? LastDigits { get; init; }
    public string? Expiry { get; init; }
    public string? CardholderName { get; init; }
}

public record SavePaymentMethodResponse
{
    public int PaymentMethodId { get; init; }
    public string? Brand { get; init; }
    public string? LastDigits { get; init; }
    public string? Expiry { get; init; }
    public string? CardholderName { get; init; }
}

public record PaymentMethodsResponse
{
    public List<PaymentMethodDto> PaymentMethods { get; init; } = new();
}

public record RefundView
{
    public string? RefundId { get; init; }
    public decimal Amount { get; init; }
    public string Status { get; init; } = string.Empty;
}

public record OrderPaymentView
{
    public int OrderId { get; init; }
    public string PaymentState { get; init; } = string.Empty;
    public decimal Amount { get; init; }
    public string Currency { get; init; } = string.Empty;
    public string? PayPalOrderId { get; init; }
    public string? AuthorizationId { get; init; }
    public string? AuthorizationStatus { get; init; }
    public string? CaptureId { get; init; }
    public string? CaptureStatus { get; init; }
    public decimal? CapturedAmount { get; init; }
    public decimal? PaypalFee { get; init; }
    public decimal? NetAmount { get; init; }
    public string? FailureReason { get; init; }
    public List<RefundView> Refunds { get; init; } = new();
}

public record MyOrderView
{
    public int OrderId { get; init; }
    public DateTimeOffset OrderDate { get; init; }
    public decimal Total { get; init; }
    public OrderPaymentView? Payment { get; init; }
}

public record MyOrdersResponse
{
    public List<MyOrderView> Orders { get; init; } = new();
}

// ---- Mapping helpers ----

public static class PaymentMapping
{
    public static CardDetails ToCardDetails(this CardDto dto) => new()
    {
        Number = dto.Number ?? string.Empty,
        Expiry = dto.Expiry ?? string.Empty,
        SecurityCode = dto.SecurityCode ?? string.Empty,
        CardholderName = dto.CardholderName,
        BillingLine1 = dto.BillingLine1,
        BillingCity = dto.BillingCity,
        BillingState = dto.BillingState,
        BillingPostalCode = dto.BillingPostalCode,
        BillingCountryCode = dto.BillingCountryCode
    };

    public static OrderPaymentView ToView(this OrderPayment payment) => new()
    {
        OrderId = payment.OrderId,
        PaymentState = payment.State.ToString(),
        Amount = payment.Amount,
        Currency = payment.Currency,
        PayPalOrderId = payment.PayPalOrderId,
        AuthorizationId = payment.AuthorizationId,
        AuthorizationStatus = payment.AuthorizationStatus,
        CaptureId = payment.CaptureId,
        CaptureStatus = payment.CaptureStatus,
        CapturedAmount = payment.CapturedAmount,
        PaypalFee = payment.PaypalFee,
        NetAmount = payment.NetAmount,
        FailureReason = payment.FailureReason,
        Refunds = payment.Refunds
            .Select(r => new RefundView { RefundId = r.PayPalRefundId, Amount = r.Amount, Status = r.Status })
            .ToList()
    };

    public static PaymentMethodDto ToDto(this PaymentMethod method) => new()
    {
        PaymentMethodId = method.Id,
        Brand = method.Brand,
        LastDigits = method.LastDigits,
        Expiry = method.Expiry,
        CardholderName = method.CardholderName
    };

    /// <summary>The caller's identity from the JWT (ClaimTypes.Name), used as buyer id.</summary>
    public static string GetBuyerId(this ClaimsPrincipal user) =>
        user.Identity?.Name
        ?? throw new ArgumentException("The authenticated caller has no name claim.");

    /// <summary>Shipping address from the request, or a placeholder when the caller omits one.</summary>
    public static Address ToAddress(this AddressDto? dto) => dto is null
        ? new Address("N/A", "N/A", "N/A", "N/A", "00000")
        : new Address(
            dto.Street ?? "N/A", dto.City ?? "N/A", dto.State ?? "N/A",
            dto.Country ?? "N/A", dto.ZipCode ?? "00000");
}
