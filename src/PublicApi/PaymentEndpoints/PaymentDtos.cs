using System;
using System.Security.Claims;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.PayPal;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>Card details for a one-off payment or for saving. Full details are passed to PayPal, never stored.</summary>
public class CardRequestDto
{
    public string Number { get; set; } = string.Empty;
    public string? CardholderName { get; set; }
    public string ExpiryMonth { get; set; } = string.Empty;   // "MM" or "M"
    public string ExpiryYear { get; set; } = string.Empty;    // "YYYY"
    public string SecurityCode { get; set; } = string.Empty;
    public string? BillingAddressLine1 { get; set; }
    public string? BillingAddressLine2 { get; set; }
    public string? BillingCity { get; set; }
    public string? BillingState { get; set; }
    public string? BillingPostalCode { get; set; }
    public string? BillingCountryCode { get; set; }           // 2-letter ISO
}

/// <summary>Shipping address for a placed order.</summary>
public class AddressDto
{
    public string Street { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string Country { get; set; } = string.Empty;
    public string ZipCode { get; set; } = string.Empty;
}

/// <summary>Maps API request DTOs to ApplicationCore inputs and reads the caller identity.</summary>
public static class PaymentMappers
{
    public static string BuyerId(ClaimsPrincipal user)
    {
        var name = user.Identity?.Name;
        Guard.Against.NullOrEmpty(name, nameof(name));
        return name;
    }

    public static CardDetails ToCardDetails(CardRequestDto dto)
    {
        Guard.Against.NullOrEmpty(dto.Number, "card number");
        Guard.Against.NullOrEmpty(dto.SecurityCode, "card security code");

        return new CardDetails(
            Number: dto.Number,
            Expiry: FormatExpiry(dto.ExpiryMonth, dto.ExpiryYear),
            SecurityCode: dto.SecurityCode,
            Name: dto.CardholderName,
            AddressLine1: dto.BillingAddressLine1,
            AddressLine2: dto.BillingAddressLine2,
            City: dto.BillingCity,
            State: dto.BillingState,
            PostalCode: dto.BillingPostalCode,
            CountryCode: dto.BillingCountryCode);
    }

    /// <summary>Formats month + year into PayPal's expected "YYYY-MM".</summary>
    public static string FormatExpiry(string month, string year)
    {
        if (!int.TryParse(month, out var m) || m < 1 || m > 12)
        {
            throw new PaymentValidationException("Card expiry month must be between 1 and 12.");
        }
        if (!int.TryParse(year, out var y) || y < 2000 || y > 2100)
        {
            throw new PaymentValidationException("Card expiry year must be a four-digit year.");
        }
        return $"{y:0000}-{m:00}";
    }
}
