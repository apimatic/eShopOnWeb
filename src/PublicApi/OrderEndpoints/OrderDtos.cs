using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class OrderDto
{
    public int OrderId { get; set; }
    public string BuyerId { get; set; } = string.Empty;
    public OrderStatus Status { get; set; }
    public DateTimeOffset OrderDate { get; set; }
    public decimal Total { get; set; }
    public string? Currency { get; set; }
    public AddressDto? ShipToAddress { get; set; }
    public List<OrderItemDto> Items { get; set; } = new();
    public PaymentDto? Payment { get; set; }

    public static OrderDto From(Order order, OrderPayment? payment, string currency) => new()
    {
        OrderId = order.Id,
        BuyerId = order.BuyerId,
        Status = order.Status,
        OrderDate = order.OrderDate,
        Total = order.Total(),
        Currency = payment?.Currency ?? currency,
        ShipToAddress = order.ShipToAddress is null ? null : new AddressDto
        {
            Street = order.ShipToAddress.Street,
            City = order.ShipToAddress.City,
            State = order.ShipToAddress.State,
            Country = order.ShipToAddress.Country,
            ZipCode = order.ShipToAddress.ZipCode
        },
        Items = order.OrderItems.Select(i => new OrderItemDto
        {
            CatalogItemId = i.ItemOrdered.CatalogItemId,
            ProductName = i.ItemOrdered.ProductName,
            UnitPrice = i.UnitPrice,
            Units = i.Units
        }).ToList(),
        Payment = payment is null ? null : PaymentDto.From(payment)
    };
}

public class OrderItemDto
{
    public int CatalogItemId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public int Units { get; set; }
}

public class AddressDto
{
    public string Street { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string? State { get; set; }
    public string Country { get; set; } = string.Empty;
    public string ZipCode { get; set; } = string.Empty;
}

/// <summary>The payment state eShop holds, mirroring what PayPal reported.</summary>
public class PaymentDto
{
    public PaymentStatus Status { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;

    /// <summary>True while a PayPal outcome is being settled (PayPal did not confirm in time).</summary>
    public bool OutcomePending { get; set; }
    public string? LastError { get; set; }
    public string? PayPalOrderId { get; set; }
    public CardSummaryDto? Card { get; set; }
    public AuthorizationDto? Authorization { get; set; }
    public CaptureDto? Capture { get; set; }
    public decimal RefundedAmount { get; set; }
    public decimal RefundableAmount { get; set; }
    public List<RefundDto> Refunds { get; set; } = new();

    public static PaymentDto From(OrderPayment p) => new()
    {
        Status = p.Status,
        Amount = p.Amount,
        Currency = p.Currency,
        OutcomePending = p.OutcomeUnknownSince is not null || p.Refunds.Any(r => r.OutcomeUnknownSince is not null),
        LastError = p.LastError,
        PayPalOrderId = p.PayPalOrderId,
        Card = p.CardLastDigits is null && p.SavedPaymentMethodId is null ? null : new CardSummaryDto
        {
            Brand = p.CardBrand,
            LastDigits = p.CardLastDigits,
            SavedPaymentMethodId = p.SavedPaymentMethodId
        },
        Authorization = p.AuthorizationId is null ? null : new AuthorizationDto
        {
            Id = p.AuthorizationId,
            Status = p.AuthorizationStatus,
            AuthorizedAt = p.AuthorizedAt,
            ExpiresAt = p.AuthorizationExpiresAt
        },
        Capture = p.CaptureId is null ? null : new CaptureDto
        {
            Id = p.CaptureId,
            Status = p.CaptureStatus,
            Amount = p.CapturedAmount,
            PayPalFee = p.PayPalFee,
            NetAmount = p.NetAmount,
            CapturedAt = p.CapturedAt
        },
        RefundedAmount = p.RefundedAmount,
        RefundableAmount = p.Status is PaymentStatus.Captured or PaymentStatus.PartiallyRefunded ? Math.Max(0, p.RefundableAmount) : 0,
        Refunds = p.Refunds.OrderBy(r => r.Id).Select(RefundDto.From).ToList()
    };
}

public class CardSummaryDto
{
    public string? Brand { get; set; }
    public string? LastDigits { get; set; }
    public int? SavedPaymentMethodId { get; set; }
}

public class AuthorizationDto
{
    public string Id { get; set; } = string.Empty;
    public string? Status { get; set; }
    public DateTimeOffset? AuthorizedAt { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
}

public class CaptureDto
{
    public string Id { get; set; } = string.Empty;
    public string? Status { get; set; }
    public decimal? Amount { get; set; }
    public decimal? PayPalFee { get; set; }
    public decimal? NetAmount { get; set; }
    public DateTimeOffset? CapturedAt { get; set; }
}

public class RefundDto
{
    public int RefundId { get; set; }
    public string? PayPalRefundId { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public RefundStatus Status { get; set; }
    public string? PayPalStatus { get; set; }
    public bool OutcomePending { get; set; }
    public string? FailureReason { get; set; }
    public DateTimeOffset RequestedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }

    public static RefundDto From(PaymentRefund r) => new()
    {
        RefundId = r.Id,
        PayPalRefundId = r.PayPalRefundId,
        IdempotencyKey = r.IdempotencyKey,
        Amount = r.Amount,
        Status = r.Status,
        PayPalStatus = r.PayPalStatus,
        OutcomePending = r.OutcomeUnknownSince is not null,
        FailureReason = r.FailureReason,
        RequestedAt = r.RequestedAt,
        CompletedAt = r.CompletedAt
    };
}

/// <summary>
/// Card details as received from the caller. Passed straight to PayPal; never stored, never logged
/// (<see cref="ToString"/> is redacted).
/// </summary>
public class CardInputDto
{
    public string? Number { get; set; }

    /// <summary>YYYY-MM.</summary>
    public string? Expiry { get; set; }
    public string? SecurityCode { get; set; }
    public string? Name { get; set; }
    public BillingAddressDto? BillingAddress { get; set; }

    public CardDetails ToCardDetails() => new(
        (Number ?? string.Empty).Replace(" ", string.Empty).Replace("-", string.Empty),
        Expiry ?? string.Empty,
        SecurityCode,
        Name,
        BillingAddress is null ? null : new CardBillingAddress(BillingAddress.AddressLine1, BillingAddress.AddressLine2,
            BillingAddress.City, BillingAddress.State, BillingAddress.PostalCode, BillingAddress.CountryCode ?? string.Empty));

    public override string ToString() => "[card details redacted]";
}

public class BillingAddressDto
{
    public string? AddressLine1 { get; set; }
    public string? AddressLine2 { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? PostalCode { get; set; }

    /// <summary>Two-letter ISO country code.</summary>
    public string? CountryCode { get; set; }
}

public class OrderResponse : BaseResponse
{
    public OrderResponse(Guid correlationId) : base(correlationId)
    {
    }

    public OrderResponse()
    {
    }

    [JsonPropertyOrder(-1)]
    public int OrderId { get; set; }
    public OrderDto? Order { get; set; }
}
