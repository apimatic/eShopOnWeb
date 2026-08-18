using System.Security.Claims;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>Card billing address supplied on a payment request. Field names mirror PayPal's model.</summary>
public class BillingAddressDto
{
    public string? CountryCode { get; set; }
    public string? AddressLine1 { get; set; }
    public string? AddressLine2 { get; set; }
    public string? AdminArea1 { get; set; } // state / province
    public string? AdminArea2 { get; set; } // city
    public string? PostalCode { get; set; }
}

/// <summary>Raw card details supplied to pay a one-off order or to save a card. Never stored or logged.</summary>
public class CardDto
{
    public string? Number { get; set; }
    public string? Expiry { get; set; } // YYYY-MM
    public string? SecurityCode { get; set; }
    public string? CardholderName { get; set; }
    public BillingAddressDto? BillingAddress { get; set; }
}

internal static class PaymentRequestMapper
{
    public static CardDetails ToCardDetails(CardDto card)
    {
        if (string.IsNullOrWhiteSpace(card.Number) ||
            string.IsNullOrWhiteSpace(card.Expiry) ||
            string.IsNullOrWhiteSpace(card.SecurityCode))
        {
            throw new PaymentValidationException("Card number, expiry (YYYY-MM) and security code are required.");
        }

        var billing = card.BillingAddress;
        var address = new PaymentCardBillingAddress(
            CountryCode: string.IsNullOrWhiteSpace(billing?.CountryCode) ? "US" : billing!.CountryCode!,
            AddressLine1: billing?.AddressLine1,
            AddressLine2: billing?.AddressLine2,
            AdminArea1: billing?.AdminArea1,
            AdminArea2: billing?.AdminArea2,
            PostalCode: billing?.PostalCode);

        return new CardDetails(card.Number!, card.Expiry!, card.SecurityCode!, card.CardholderName, address);
    }

    /// <summary>The caller's identity (username) from the validated JWT — the owner scope for the action.</summary>
    public static string GetBuyerId(ClaimsPrincipal user)
    {
        var name = user.Identity?.Name;
        if (string.IsNullOrEmpty(name))
            throw new PaymentValidationException("The caller's identity could not be determined from the token.");
        return name;
    }
}
