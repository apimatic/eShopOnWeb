using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.PayPal;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

// ---- request DTOs -----------------------------------------------------------------------

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

/// <summary>Raw card details. Transient only — never stored or logged by this app.</summary>
public class CardDto
{
    public string Number { get; set; } = string.Empty;

    /// <summary>Expiry as "YYYY-MM", "MM/YY" or "MM/YYYY"; normalised to PayPal's "YYYY-MM".</summary>
    public string Expiry { get; set; } = string.Empty;

    public string? SecurityCode { get; set; }
    public string? Name { get; set; }
    public string? BillingAddressLine1 { get; set; }
    public string? BillingAddressLine2 { get; set; }
    public string? BillingState { get; set; }
    public string? BillingCity { get; set; }
    public string? BillingPostalCode { get; set; }
    public string? BillingCountryCode { get; set; }

    public CardDetails ToCardDetails() => new()
    {
        Number = (Number ?? string.Empty).Replace(" ", string.Empty),
        Expiry = NormalizeExpiry(Expiry),
        SecurityCode = SecurityCode,
        Name = Name,
        BillingAddressLine1 = BillingAddressLine1,
        BillingAddressLine2 = BillingAddressLine2,
        BillingAdminArea1 = BillingState,
        BillingAdminArea2 = BillingCity,
        BillingPostalCode = BillingPostalCode,
        BillingCountryCode = BillingCountryCode
    };

    /// <summary>Accepts "YYYY-MM", "MM/YY" or "MM/YYYY" and returns PayPal's "YYYY-MM".</summary>
    public static string NormalizeExpiry(string expiry)
    {
        var raw = (expiry ?? string.Empty).Trim();
        if (raw.Length == 7 && raw[4] == '-')
        {
            return raw; // already YYYY-MM
        }
        var parts = raw.Split('/', '-');
        if (parts.Length == 2)
        {
            var month = parts[0].PadLeft(2, '0');
            var year = parts[1];
            if (year.Length == 2)
            {
                year = "20" + year;
            }
            return $"{year}-{month}";
        }
        return raw; // let PayPal validate anything unexpected
    }
}

public class PayOrderRequest
{
    public CardDto? Card { get; set; }
    public int? SavedPaymentMethodId { get; set; }
}

public class SaveCardRequest
{
    public CardDto Card { get; set; } = new();
}

public class RefundRequest
{
    /// <summary>Amount to refund; omit for the full remaining amount.</summary>
    public decimal? Amount { get; set; }

    /// <summary>Caller-supplied idempotency key; repeating a request under the same key does not refund twice.</summary>
    public string IdempotencyKey { get; set; } = string.Empty;
}

// ---- response DTOs ----------------------------------------------------------------------

public class RefundDto
{
    public int Id { get; set; }
    public string PayPalRefundId { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}

public class PaymentDto
{
    public string PayPalOrderId { get; set; } = string.Empty;
    public string AuthorizationId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string? CaptureId { get; set; }
    public decimal? CapturedAmount { get; set; }
    public decimal? PayPalFee { get; set; }
    public decimal? NetAmount { get; set; }
    public decimal TotalRefunded { get; set; }
    public int? SavedPaymentMethodId { get; set; }
    public List<RefundDto> Refunds { get; set; } = new();
}

public class OrderPaymentDto
{
    public int OrderId { get; set; }
    public string OrderStatus { get; set; } = string.Empty;
    public decimal Total { get; set; }
    public DateTimeOffset OrderDate { get; set; }
    public PaymentDto? Payment { get; set; }

    public static OrderPaymentDto From(OrderPaymentView view)
    {
        var dto = new OrderPaymentDto
        {
            OrderId = view.Order.Id,
            OrderStatus = view.Order.Status.ToString(),
            Total = view.Order.Total(),
            OrderDate = view.Order.OrderDate
        };
        if (view.Payment is not null)
        {
            var p = view.Payment;
            dto.Payment = new PaymentDto
            {
                PayPalOrderId = p.PayPalOrderId,
                AuthorizationId = p.AuthorizationId,
                Status = p.Status.ToString(),
                Amount = p.Amount,
                Currency = p.Currency,
                CaptureId = p.CaptureId,
                CapturedAmount = p.CapturedAmount,
                PayPalFee = p.PayPalFee,
                NetAmount = p.NetAmount,
                TotalRefunded = p.TotalRefunded,
                SavedPaymentMethodId = p.PaymentMethodId,
                Refunds = p.Refunds
                    .OrderBy(r => r.CreatedAt)
                    .Select(r => new RefundDto
                    {
                        Id = r.Id,
                        PayPalRefundId = r.PayPalRefundId,
                        Amount = r.Amount,
                        Status = r.Status.ToString(),
                        CreatedAt = r.CreatedAt
                    }).ToList()
            };
        }
        return dto;
    }
}

public class PlaceOrderResponse
{
    public int OrderId { get; set; }
    public decimal Total { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
}

public class RefundResponse
{
    public int RefundId { get; set; }
    public OrderPaymentDto Order { get; set; } = new();
}

public class PaymentMethodDto
{
    public int PaymentMethodId { get; set; }
    public string Brand { get; set; } = string.Empty;
    public string LastFourDigits { get; set; } = string.Empty;
    public string Expiry { get; set; } = string.Empty;
    public string? CardholderName { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public static PaymentMethodDto From(PaymentMethod method) => new()
    {
        PaymentMethodId = method.Id,
        Brand = method.Brand,
        LastFourDigits = method.LastFourDigits,
        Expiry = method.Expiry,
        CardholderName = method.CardholderName,
        CreatedAt = method.CreatedAt
    };
}

public class SaveCardResponse
{
    public int PaymentMethodId { get; set; }
    public PaymentMethodDto Card { get; set; } = new();
}

// ---- helpers ----------------------------------------------------------------------------

public static class CallerExtensions
{
    /// <summary>The authenticated caller's identity string, used as the order/card BuyerId.</summary>
    public static string GetBuyerId(this ClaimsPrincipal user)
    {
        var name = user.Identity?.Name ?? user.FindFirstValue(ClaimTypes.Name);
        if (string.IsNullOrEmpty(name))
        {
            throw new ForbiddenException("The caller identity could not be determined.");
        }
        return name;
    }
}
