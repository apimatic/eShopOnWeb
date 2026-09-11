using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.PayPal;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>Shopper-supplied card details for a one-off charge or to be vaulted.</summary>
public class CardRequest
{
    public string Number { get; set; } = string.Empty;
    /// <summary>Card expiry as year-month, e.g. "2030-04" (per PayPal's date_year_month).</summary>
    public string Expiry { get; set; } = string.Empty;
    public string SecurityCode { get; set; } = string.Empty;
    public string? Name { get; set; }
    public BillingAddressRequest? BillingAddress { get; set; }
}

/// <summary>Shopper-friendly billing address, mapped to PayPal's portable address shape.</summary>
public class BillingAddressRequest
{
    public string? Line1 { get; set; }
    public string? Line2 { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? PostalCode { get; set; }
    /// <summary>Two-letter country code, e.g. "US".</summary>
    public string? CountryCode { get; set; }
}

/// <summary>Projection of a <see cref="Payment"/> for API responses.</summary>
public class PaymentDto
{
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string InvoiceId { get; set; } = string.Empty;
    public string? PayPalOrderId { get; set; }
    public AuthorizationDto? Authorization { get; set; }
    public CaptureDto? Capture { get; set; }
    public List<RefundDto> Refunds { get; set; } = new();

    public class AuthorizationDto
    {
        public string Id { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public DateTimeOffset? ExpiresAt { get; set; }
    }

    public class CaptureDto
    {
        public string Id { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public decimal Gross { get; set; }
        public decimal PayPalFee { get; set; }
        public decimal Net { get; set; }
    }

    public class RefundDto
    {
        public string? RefundId { get; set; }
        public decimal Amount { get; set; }
        public string Status { get; set; } = string.Empty;
    }
}

/// <summary>Safe description of a saved card.</summary>
public class SavedCardDto
{
    public int PaymentMethodId { get; set; }
    public string Brand { get; set; } = string.Empty;
    public string LastDigits { get; set; } = string.Empty;
    public string Expiry { get; set; } = string.Empty;
    public string? CardholderName { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>Mapping and identity helpers shared across the payment endpoints.</summary>
public static class PaymentMapping
{
    /// <summary>The signed-in shopper's stable id (their user name / email from the token).</summary>
    public static string GetBuyerId(this ClaimsPrincipal? user)
    {
        var id = user?.Identity?.Name
                 ?? user?.FindFirstValue(ClaimTypes.Name)
                 ?? user?.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(id))
            throw new UnauthorizedAccessException("The request is not associated with an authenticated shopper.");
        return id;
    }

    public static CardDetails ToCardDetails(this CardRequest card) => new(
        card.Number, card.Expiry, card.SecurityCode, card.Name,
        card.BillingAddress is null ? null : new PayPalAddress(
            card.BillingAddress.Line1, card.BillingAddress.Line2, card.BillingAddress.City,
            card.BillingAddress.State, card.BillingAddress.PostalCode, card.BillingAddress.CountryCode));

    public static PaymentDto ToDto(this Payment p)
    {
        var dto = new PaymentDto
        {
            OrderId = p.OrderId,
            Status = p.Status.ToString(),
            Amount = p.Amount,
            Currency = p.CurrencyCode,
            InvoiceId = p.InvoiceId,
            PayPalOrderId = p.PayPalOrderId,
            Refunds = p.Refunds.Select(r => new PaymentDto.RefundDto
            {
                RefundId = r.PayPalRefundId,
                Amount = r.Amount,
                Status = r.Status
            }).ToList()
        };

        if (p.AuthorizationId is not null)
            dto.Authorization = new PaymentDto.AuthorizationDto
            {
                Id = p.AuthorizationId,
                Status = p.AuthorizationStatus ?? string.Empty,
                ExpiresAt = p.AuthorizationExpiresAt
            };

        if (p.CaptureId is not null)
            dto.Capture = new PaymentDto.CaptureDto
            {
                Id = p.CaptureId,
                Status = p.CaptureStatus ?? string.Empty,
                Gross = p.CapturedGross ?? 0m,
                PayPalFee = p.PayPalFee ?? 0m,
                Net = p.NetAmount ?? 0m
            };

        return dto;
    }

    public static SavedCardDto ToDto(this SavedPaymentMethod m) => new()
    {
        PaymentMethodId = m.Id,
        Brand = m.Brand,
        LastDigits = m.LastDigits,
        Expiry = m.ExpiryMonthYear,
        CardholderName = m.CardholderName,
        CreatedAt = m.CreatedAt
    };
}
