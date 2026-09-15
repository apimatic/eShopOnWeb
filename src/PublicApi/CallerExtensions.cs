using System;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.PayPal;

namespace Microsoft.eShopWeb.PublicApi;

/// <summary>Helpers for turning the authenticated request into the identity/values endpoints act on.</summary>
public static class CallerExtensions
{
    /// <summary>
    /// The caller's buyer id, taken from the JWT name claim. Every shopper-scoped endpoint keys its
    /// data off this so one shopper can never see or act on another's.
    /// </summary>
    public static string GetBuyerId(this HttpContext http)
    {
        var name = http.User?.Identity?.Name
            ?? http.User?.FindFirstValue(ClaimTypes.Name);
        if (string.IsNullOrEmpty(name))
        {
            throw new UnauthorizedAccessException("The request is not authenticated.");
        }
        return name;
    }

    /// <summary>Maps the application's card input from an inbound card request.</summary>
    public static CardInput ToCardInput(this CardRequest card) => new(
        card.Number,
        card.Expiry,
        card.SecurityCode,
        card.Name,
        card.BillingAddress == null
            ? null
            : new BillingAddressInput(card.BillingAddress.Line1, card.BillingAddress.Line2,
                card.BillingAddress.City, card.BillingAddress.State, card.BillingAddress.PostalCode,
                card.BillingAddress.CountryCode));
}
