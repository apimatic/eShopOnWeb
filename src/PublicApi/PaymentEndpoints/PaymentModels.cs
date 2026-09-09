using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

// ------------------------------ Requests ------------------------------

/// <summary>A card supplied by the shopper. Forwarded to PayPal; never stored by this app.</summary>
public class CardRequest
{
    public string Number { get; set; } = string.Empty;
    /// <summary>Expiry in YYYY-MM format (e.g. 2030-01).</summary>
    public string Expiry { get; set; } = string.Empty;
    public string? SecurityCode { get; set; }
    public string? Name { get; set; }
    public BillingAddressRequest? BillingAddress { get; set; }

    public CardDetails ToCardDetails() => new()
    {
        Number = Number,
        Expiry = Expiry,
        SecurityCode = SecurityCode,
        Name = Name,
        BillingAddress = BillingAddress?.ToDomain()
    };
}

public class BillingAddressRequest
{
    public string? AddressLine1 { get; set; }
    public string? AddressLine2 { get; set; }
    public string? AdminArea2 { get; set; }
    public string? AdminArea1 { get; set; }
    public string? PostalCode { get; set; }
    public string CountryCode { get; set; } = "US";

    public CardBillingAddress ToDomain() => new()
    {
        AddressLine1 = AddressLine1,
        AddressLine2 = AddressLine2,
        AdminArea2 = AdminArea2,
        AdminArea1 = AdminArea1,
        PostalCode = PostalCode,
        CountryCode = CountryCode
    };
}

public class AddressRequest
{
    public string Street { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string Country { get; set; } = string.Empty;
    public string ZipCode { get; set; } = string.Empty;

    public Address ToDomain() => new(Street, City, State, Country, ZipCode);
}

public class PlaceOrderRequest
{
    public List<OrderLineDto> Items { get; set; } = new();
    public AddressRequest? ShipToAddress { get; set; }
}

public class OrderLineDto
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

public class PayOrderRequest
{
    /// <summary>Raw card for a one-off payment. Provide this OR <see cref="PaymentMethodId"/>.</summary>
    public CardRequest? Card { get; set; }

    /// <summary>The id of one of the shopper's saved cards to pay with instead.</summary>
    public int? PaymentMethodId { get; set; }
}

public class RefundRequest
{
    /// <summary>Amount to refund. Omit for a full refund of the remaining captured amount.</summary>
    public decimal? Amount { get; set; }

    /// <summary>Caller-supplied idempotency key. Repeating a request under the same key never refunds twice.</summary>
    public string IdempotencyKey { get; set; } = string.Empty;
}

public class SavePaymentMethodRequest
{
    public CardRequest Card { get; set; } = new();
    public string? Alias { get; set; }
}

// ------------------------------ Responses ------------------------------

public class RefundDto
{
    public int Id { get; set; }
    public string PayPalRefundId { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }

    public static RefundDto From(PaymentRefund r) => new()
    {
        Id = r.Id,
        PayPalRefundId = r.PayPalRefundId,
        Amount = r.Amount,
        Status = r.Status,
        CreatedAt = r.CreatedAt
    };
}

public class PaymentStateDto
{
    public string Currency { get; set; } = string.Empty;
    public decimal AuthorizedAmount { get; set; }
    public string Status { get; set; } = string.Empty;
    public string PayPalOrderId { get; set; } = string.Empty;
    public string AuthorizationId { get; set; } = string.Empty;
    public string AuthorizationStatus { get; set; } = string.Empty;
    public DateTimeOffset? AuthorizationExpiresAt { get; set; }
    public string? CaptureId { get; set; }
    public string? CaptureStatus { get; set; }
    public decimal? CapturedAmount { get; set; }
    public decimal? PayPalFee { get; set; }
    public decimal? NetAmount { get; set; }
    public decimal TotalRefunded { get; set; }
    public decimal RefundableRemaining { get; set; }
    public List<RefundDto> Refunds { get; set; } = new();

    public static PaymentStateDto? From(Payment? p)
    {
        if (p is null) return null;
        return new PaymentStateDto
        {
            Currency = p.Currency,
            AuthorizedAmount = p.AuthorizedAmount,
            Status = p.Status.ToString(),
            PayPalOrderId = p.PayPalOrderId,
            AuthorizationId = p.AuthorizationId,
            AuthorizationStatus = p.AuthorizationStatus,
            AuthorizationExpiresAt = p.AuthorizationExpiresAt,
            CaptureId = p.CaptureId,
            CaptureStatus = p.CaptureStatus,
            CapturedAmount = p.CapturedAmount,
            PayPalFee = p.PayPalFee,
            NetAmount = p.NetAmount,
            TotalRefunded = p.TotalRefunded(),
            RefundableRemaining = p.RefundableRemaining(),
            Refunds = p.Refunds.Select(RefundDto.From).ToList()
        };
    }
}

public class OrderDto
{
    public int OrderId { get; set; }
    public string BuyerId { get; set; } = string.Empty;
    public DateTimeOffset OrderDate { get; set; }
    public decimal Total { get; set; }
    public string Status { get; set; } = string.Empty;
    public List<OrderItemDto> Items { get; set; } = new();
    public PaymentStateDto? Payment { get; set; }

    public static OrderDto From(Order o) => new()
    {
        OrderId = o.Id,
        BuyerId = o.BuyerId,
        OrderDate = o.OrderDate,
        Total = o.Total(),
        Status = o.Status.ToString(),
        Items = o.OrderItems.Select(i => new OrderItemDto
        {
            CatalogItemId = i.ItemOrdered.CatalogItemId,
            ProductName = i.ItemOrdered.ProductName,
            UnitPrice = i.UnitPrice,
            Units = i.Units
        }).ToList(),
        Payment = PaymentStateDto.From(o.Payment)
    };
}

public class OrderItemDto
{
    public int CatalogItemId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public int Units { get; set; }
}

public class PaymentMethodDto
{
    public int PaymentMethodId { get; set; }
    public string? Brand { get; set; }
    public string? Last4 { get; set; }
    public string? Expiry { get; set; }
    public string? Alias { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public static PaymentMethodDto From(Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate.PaymentMethod pm) => new()
    {
        PaymentMethodId = pm.Id,
        Brand = pm.Brand,
        Last4 = pm.Last4,
        Expiry = pm.Expiry,
        Alias = pm.Alias,
        CreatedAt = pm.CreatedAt
    };
}
