using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>Card details supplied for a one-off payment or for saving. Never stored or logged.</summary>
public class CardModel
{
    public string Number { get; set; } = string.Empty;
    public int ExpiryMonth { get; set; }
    public int ExpiryYear { get; set; }
    public string? SecurityCode { get; set; }
    public string? Name { get; set; }
    public BillingAddressModel? BillingAddress { get; set; }

    public PayPalCardDetails ToDetails()
    {
        if (string.IsNullOrWhiteSpace(Number))
        {
            throw new PaymentException("Card number is required.", PaymentErrorReason.Validation);
        }
        if (ExpiryMonth is < 1 or > 12)
        {
            throw new PaymentException("Card expiry month must be between 1 and 12.", PaymentErrorReason.Validation);
        }
        if (ExpiryYear < 2000 || ExpiryYear > 2100)
        {
            throw new PaymentException("Card expiry year is invalid.", PaymentErrorReason.Validation);
        }

        var expiry = $"{ExpiryYear:D4}-{ExpiryMonth:D2}";
        return new PayPalCardDetails(Number.Trim(), expiry, SecurityCode, Name, BillingAddress?.ToDetails());
    }
}

public class BillingAddressModel
{
    public string? AddressLine1 { get; set; }
    public string? AddressLine2 { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? PostalCode { get; set; }
    public string? CountryCode { get; set; }

    public PayPalBillingAddress ToDetails() =>
        new(AddressLine1, AddressLine2, City, State, PostalCode, CountryCode);
}

// -------- Response DTOs --------

public class OrderSummaryDto
{
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public decimal Total { get; set; }
    public string? Currency { get; set; }
    public DateTimeOffset OrderDate { get; set; }
    public List<OrderLineDto> Items { get; set; } = new();
    public PaymentDto? Payment { get; set; }
}

public class OrderLineDto
{
    public int CatalogItemId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public int Units { get; set; }
}

public class PaymentDto
{
    public decimal AuthorizedAmount { get; set; }
    public string Currency { get; set; } = string.Empty;

    public string? PayPalOrderId { get; set; }
    public string? AuthorizationId { get; set; }
    public string? AuthorizationStatus { get; set; }
    public DateTimeOffset? AuthorizationExpiresAt { get; set; }

    public string? CaptureId { get; set; }
    public string? CaptureStatus { get; set; }
    public decimal? CapturedAmount { get; set; }
    public decimal? PayPalFee { get; set; }
    public decimal? NetAmount { get; set; }
    public DateTimeOffset? CapturedAt { get; set; }

    public decimal RefundedAmount { get; set; }
    public decimal RefundableRemaining { get; set; }
    public List<RefundDto> Refunds { get; set; } = new();
}

public class RefundDto
{
    public string RefundId { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}

public class SavedCardDto
{
    public int PaymentMethodId { get; set; }
    public string CardBrand { get; set; } = string.Empty;
    public string LastFourDigits { get; set; } = string.Empty;
    public string Expiry { get; set; } = string.Empty;
    public string? CardholderName { get; set; }
    public DateTimeOffset CreatedDate { get; set; }
}

/// <summary>Maps domain entities to safe API DTOs.</summary>
public static class PaymentDtoMapper
{
    public static OrderSummaryDto ToDto(Order order)
    {
        return new OrderSummaryDto
        {
            OrderId = order.Id,
            Status = order.Status.ToString(),
            Total = order.Total(),
            Currency = order.Payment?.Currency,
            OrderDate = order.OrderDate,
            Items = order.OrderItems.Select(i => new OrderLineDto
            {
                CatalogItemId = i.ItemOrdered.CatalogItemId,
                ProductName = i.ItemOrdered.ProductName,
                UnitPrice = i.UnitPrice,
                Units = i.Units
            }).ToList(),
            Payment = order.Payment is null ? null : ToDto(order.Payment)
        };
    }

    public static PaymentDto ToDto(OrderPayment p) => new()
    {
        AuthorizedAmount = p.Amount,
        Currency = p.Currency,
        PayPalOrderId = p.PayPalOrderId,
        AuthorizationId = p.AuthorizationId,
        AuthorizationStatus = p.AuthorizationStatus,
        AuthorizationExpiresAt = p.AuthorizationExpiresAt,
        CaptureId = p.CaptureId,
        CaptureStatus = p.CaptureStatus,
        CapturedAmount = p.CapturedAmount,
        PayPalFee = p.PayPalFee,
        NetAmount = p.NetAmount,
        CapturedAt = p.CapturedAt,
        RefundedAmount = p.RefundedAmount,
        RefundableRemaining = p.RefundableRemaining,
        Refunds = p.Refunds.Select(ToDto).ToList()
    };

    public static RefundDto ToDto(OrderRefund r) => new()
    {
        RefundId = r.RefundId,
        Amount = r.Amount,
        Status = r.Status,
        CreatedAt = r.CreatedAt
    };

    public static SavedCardDto ToDto(SavedPaymentMethod pm) => new()
    {
        PaymentMethodId = pm.Id,
        CardBrand = pm.CardBrand,
        LastFourDigits = pm.LastFourDigits,
        Expiry = pm.Expiry,
        CardholderName = pm.CardholderName,
        CreatedDate = pm.CreatedDate
    };
}

public static class ClaimsPrincipalExtensions
{
    /// <summary>The caller's identity (username), used as the order/card owner (BuyerId).</summary>
    public static string GetBuyerId(this ClaimsPrincipal user)
    {
        var id = user.FindFirstValue(ClaimTypes.Name) ?? user.Identity?.Name;
        if (string.IsNullOrEmpty(id))
        {
            throw new PaymentException("The bearer token does not identify a user.", PaymentErrorReason.Validation);
        }
        return id;
    }
}
