using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.PayPal;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

// ---------- Request shapes ----------

/// <summary>Card details supplied by the caller. Never stored in this app's database nor logged.</summary>
public class CardRequest
{
    public string Number { get; set; } = string.Empty;
    /// <summary>Card expiry as ISO year-month, e.g. "2028-04".</summary>
    public string Expiry { get; set; } = string.Empty;
    public string? SecurityCode { get; set; }
    public string? CardholderName { get; set; }
    public BillingAddressRequest? BillingAddress { get; set; }

    public CardDetails ToCardDetails() => new(
        Number?.Replace(" ", string.Empty) ?? string.Empty,
        Expiry,
        SecurityCode,
        CardholderName,
        BillingAddress?.ToDomain());
}

public class BillingAddressRequest
{
    public string? AddressLine1 { get; set; }
    public string? AddressLine2 { get; set; }
    public string? AdminArea2 { get; set; }
    public string? AdminArea1 { get; set; }
    public string? PostalCode { get; set; }
    public string? CountryCode { get; set; }

    public CardBillingAddress ToDomain() =>
        new(AddressLine1, AddressLine2, AdminArea2, AdminArea1, PostalCode, CountryCode);
}

// ---------- Response DTOs (safe: never full card details) ----------

public class RefundDto
{
    public string RefundId { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }

    public static RefundDto From(PaymentRefund r) => new()
    {
        RefundId = r.PayPalRefundId,
        Amount = r.Amount,
        Currency = r.CurrencyCode,
        Status = r.Status,
        CreatedAt = r.CreatedAt
    };
}

public class PaymentDto
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
    public decimal? CapturedAmount { get; set; }
    public decimal? PayPalFee { get; set; }
    public decimal? NetAmount { get; set; }

    public decimal RefundedAmount { get; set; }
    public decimal RefundableAmount { get; set; }
    public int? SavedPaymentMethodId { get; set; }
    public List<RefundDto> Refunds { get; set; } = new();

    public static PaymentDto From(Payment p) => new()
    {
        OrderId = p.OrderId,
        Status = p.Status.ToString(),
        Amount = p.Amount,
        Currency = p.CurrencyCode,
        PayPalOrderId = p.PayPalOrderId,
        AuthorizationId = p.AuthorizationId,
        AuthorizationStatus = p.AuthorizationStatus,
        AuthorizationExpiresAt = p.AuthorizationExpiresAt,
        CaptureId = p.CaptureId,
        CaptureStatus = p.CaptureStatus,
        CapturedAmount = p.CapturedAmount,
        PayPalFee = p.PayPalFee,
        NetAmount = p.NetAmount,
        RefundedAmount = p.RefundedAmount(),
        RefundableAmount = p.RefundableAmount(),
        SavedPaymentMethodId = p.SavedPaymentMethodId,
        Refunds = p.Refunds.Select(RefundDto.From).ToList()
    };
}

public class SavedPaymentMethodDto
{
    public int PaymentMethodId { get; set; }
    public string Brand { get; set; } = string.Empty;
    public string LastDigits { get; set; } = string.Empty;
    public string? Expiry { get; set; }
    public string? CardholderName { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public static SavedPaymentMethodDto From(SavedPaymentMethod m) => new()
    {
        PaymentMethodId = m.Id,
        Brand = m.Brand,
        LastDigits = m.LastDigits,
        Expiry = m.Expiry,
        CardholderName = m.CardholderName,
        CreatedAt = m.CreatedAt
    };
}

public class OrderItemDto
{
    public int CatalogItemId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public int Units { get; set; }
}

public class OrderWithPaymentDto
{
    public int OrderId { get; set; }
    public DateTimeOffset OrderDate { get; set; }
    public decimal Total { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string PaymentStatus { get; set; } = string.Empty;
    public string? AuthorizationId { get; set; }
    public string? CaptureId { get; set; }
    public decimal? CapturedAmount { get; set; }
    public decimal RefundedAmount { get; set; }
    public List<OrderItemDto> Items { get; set; } = new();

    public static OrderWithPaymentDto From(OrderWithPayment ow)
    {
        var order = ow.Order;
        var payment = ow.Payment;
        return new OrderWithPaymentDto
        {
            OrderId = order.Id,
            OrderDate = order.OrderDate,
            Total = order.Total(),
            Currency = payment?.CurrencyCode ?? string.Empty,
            PaymentStatus = (payment?.Status
                ?? Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate.PaymentStatus.PendingPayment).ToString(),
            AuthorizationId = payment?.AuthorizationId,
            CaptureId = payment?.CaptureId,
            CapturedAmount = payment?.CapturedAmount,
            RefundedAmount = payment?.RefundedAmount() ?? 0m,
            Items = order.OrderItems.Select(i => new OrderItemDto
            {
                CatalogItemId = i.ItemOrdered.CatalogItemId,
                ProductName = i.ItemOrdered.ProductName,
                UnitPrice = i.UnitPrice,
                Units = i.Units
            }).ToList()
        };
    }
}
