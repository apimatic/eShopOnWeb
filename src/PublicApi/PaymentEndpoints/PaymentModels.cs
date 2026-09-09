using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.PayPal;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>Card details for a one-off payment or for vaulting. Never stored or logged by the app.</summary>
public class CardModel
{
    public string Number { get; set; } = string.Empty;

    /// <summary>Expiry in YYYY-MM form, per PayPal's card schema.</summary>
    public string Expiry { get; set; } = string.Empty;

    public string SecurityCode { get; set; } = string.Empty;
    public string? Name { get; set; }
    public BillingAddressModel? BillingAddress { get; set; }

    public PayPalCardInput ToPayPalCardInput() => new(
        Number, Expiry, SecurityCode, Name,
        BillingAddress?.ToPayPalBillingAddress());
}

/// <summary>Portable billing address for a card.</summary>
public class BillingAddressModel
{
    public string AddressLine1 { get; set; } = string.Empty;
    public string? AddressLine2 { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? PostalCode { get; set; }
    public string CountryCode { get; set; } = string.Empty;

    public PayPalBillingAddress ToPayPalBillingAddress() =>
        new(AddressLine1, AddressLine2, City, State, PostalCode, CountryCode);
}

/// <summary>A refund taken against the payment.</summary>
public class RefundModel
{
    public int Id { get; set; }
    public string RefundId { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Status { get; set; } = string.Empty;
    public string IdempotencyKey { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>The full payment state for an order, including everything PayPal owns.</summary>
public class PaymentStateModel
{
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;

    public string? PayPalOrderId { get; set; }
    public string? AuthorizationId { get; set; }
    public string? AuthorizationStatus { get; set; }
    public string? CaptureId { get; set; }
    public string? CaptureStatus { get; set; }
    public decimal? CapturedAmount { get; set; }
    public decimal? PayPalFee { get; set; }
    public decimal? NetAmount { get; set; }

    public string? CardBrand { get; set; }
    public string? CardLastFour { get; set; }

    public decimal TotalRefunded { get; set; }
    public decimal RefundableRemaining { get; set; }
    public string? FailureReason { get; set; }

    public List<RefundModel> Refunds { get; set; } = new();

    public static PaymentStateModel? From(Payment? payment)
    {
        if (payment is null)
        {
            return null;
        }
        return new PaymentStateModel
        {
            OrderId = payment.OrderId,
            Status = payment.Status.ToString(),
            Amount = payment.Amount,
            Currency = payment.Currency,
            PayPalOrderId = payment.PayPalOrderId,
            AuthorizationId = payment.AuthorizationId,
            AuthorizationStatus = payment.AuthorizationStatus,
            CaptureId = payment.CaptureId,
            CaptureStatus = payment.CaptureStatus,
            CapturedAmount = payment.CapturedAmount,
            PayPalFee = payment.PayPalFee,
            NetAmount = payment.NetAmount,
            CardBrand = payment.CardBrand,
            CardLastFour = payment.CardLastFour,
            TotalRefunded = payment.TotalRefunded(),
            RefundableRemaining = payment.RefundableRemaining(),
            FailureReason = payment.FailureReason,
            Refunds = payment.Refunds
                .OrderBy(r => r.CreatedAt)
                .Select(r => new RefundModel
                {
                    Id = r.Id,
                    RefundId = r.PayPalRefundId,
                    Amount = r.Amount,
                    Status = r.Status,
                    IdempotencyKey = r.IdempotencyKey,
                    CreatedAt = r.CreatedAt
                }).ToList()
        };
    }
}

/// <summary>A line of an order.</summary>
public class OrderItemModel
{
    public int CatalogItemId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public int Units { get; set; }
}

/// <summary>An order together with its payment state.</summary>
public class OrderModel
{
    public int OrderId { get; set; }
    public DateTimeOffset OrderDate { get; set; }
    public decimal Total { get; set; }
    public List<OrderItemModel> Items { get; set; } = new();
    public PaymentStateModel? Payment { get; set; }

    public static OrderModel From(Order order, Payment? payment) => new()
    {
        OrderId = order.Id,
        OrderDate = order.OrderDate,
        Total = order.Total(),
        Items = order.OrderItems.Select(i => new OrderItemModel
        {
            CatalogItemId = i.ItemOrdered.CatalogItemId,
            ProductName = i.ItemOrdered.ProductName,
            UnitPrice = i.UnitPrice,
            Units = i.Units
        }).ToList(),
        Payment = PaymentStateModel.From(payment)
    };
}

/// <summary>A saved card, described safely for recognition — never full details.</summary>
public class SavedCardModel
{
    public int PaymentMethodId { get; set; }
    public string CardBrand { get; set; } = string.Empty;
    public string LastFour { get; set; } = string.Empty;
    public string Expiry { get; set; } = string.Empty;
    public string? CardHolderName { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public static SavedCardModel From(SavedCard card) => new()
    {
        PaymentMethodId = card.Id,
        CardBrand = card.CardBrand,
        LastFour = card.LastFour,
        Expiry = card.Expiry,
        CardHolderName = card.CardHolderName,
        CreatedAt = card.CreatedAt
    };
}

/// <summary>Shared helper to resolve the caller's identity (the buyer id) from the token.</summary>
public static class CallerIdentity
{
    public static string GetBuyerId(ClaimsPrincipal? user)
    {
        var name = user?.Identity?.Name;
        if (string.IsNullOrEmpty(name))
        {
            throw new UnauthorizedAccessException("The caller's identity could not be determined from the token.");
        }
        return name;
    }
}
