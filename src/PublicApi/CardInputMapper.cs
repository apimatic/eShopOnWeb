using System;
using System.Globalization;
using System.Linq;
using System.Security.Claims;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.PublicApi;

internal static class HttpUserExtensions
{
    public static string RequireBuyerId(this ClaimsPrincipal user)
    {
        var name = user.Identity?.Name;
        if (string.IsNullOrWhiteSpace(name))
            throw new UnauthorizedAccessException("The caller is not authenticated.");
        return name;
    }
}

internal static class CardInputMapper
{
    public static CardPaymentInput Map(CardDetailsRequest card)
    {
        if (card == null)
            throw new ArgumentException("Card details are required.");
        if (string.IsNullOrWhiteSpace(card.Number) || string.IsNullOrWhiteSpace(card.Expiry) || string.IsNullOrWhiteSpace(card.SecurityCode))
            throw new ArgumentException("Card number, expiry, and security code are required.");

        return new CardPaymentInput
        {
            Number = new string(card.Number.Where(char.IsDigit).ToArray()),
            Expiry = NormalizeExpiry(card.Expiry),
            SecurityCode = card.SecurityCode.Trim(),
            Name = card.Name,
            BillingAddress = card.BillingAddress == null
                ? null
                : new CardBillingAddress
                {
                    AddressLine1 = card.BillingAddress.AddressLine1,
                    AddressLine2 = card.BillingAddress.AddressLine2,
                    AdminArea2 = card.BillingAddress.AdminArea2,
                    AdminArea1 = card.BillingAddress.AdminArea1,
                    PostalCode = card.BillingAddress.PostalCode,
                    CountryCode = card.BillingAddress.CountryCode
                }
        };
    }

    public static string NormalizeExpiry(string expiry)
    {
        expiry = expiry.Trim();
        if (DateTime.TryParseExact(expiry, "yyyy-MM", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
            return expiry;
        if (DateTime.TryParseExact(expiry, "MM/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var monthYear))
            return monthYear.ToString("yyyy-MM", CultureInfo.InvariantCulture);
        if (DateTime.TryParseExact(expiry, "MM/yy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var shortYear))
            return shortYear.ToString("yyyy-MM", CultureInfo.InvariantCulture);
        throw new ArgumentException("Card expiry must be YYYY-MM.");
    }
}

public class CardDetailsRequest
{
    public string Number { get; set; } = string.Empty;
    public string Expiry { get; set; } = string.Empty;
    public string SecurityCode { get; set; } = string.Empty;
    public string? Name { get; set; }
    public BillingAddressRequest? BillingAddress { get; set; }
}

public class BillingAddressRequest
{
    public string? AddressLine1 { get; set; }
    public string? AddressLine2 { get; set; }
    public string? AdminArea2 { get; set; }
    public string? AdminArea1 { get; set; }
    public string? PostalCode { get; set; }
    public string CountryCode { get; set; } = "US";
}
