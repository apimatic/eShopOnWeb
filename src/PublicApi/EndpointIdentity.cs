using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.PublicApi;

internal static class EndpointIdentity
{
    public static string RequireUserName(HttpContext httpContext)
    {
        var name = httpContext.User.Identity?.Name
                   ?? httpContext.User.FindFirstValue(ClaimTypes.Name);
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new CheckoutException("The caller is not authenticated.", 401);
        }

        return name;
    }

    public static Address ToAddress(OrderEndpoints.ShipToAddressRequest? request)
    {
        if (request == null
            || string.IsNullOrWhiteSpace(request.Street)
            || string.IsNullOrWhiteSpace(request.City)
            || string.IsNullOrWhiteSpace(request.Country)
            || string.IsNullOrWhiteSpace(request.ZipCode))
        {
            return new Address("123 Main St.", "Kent", "OH", "United States", "44240");
        }

        return new Address(request.Street, request.City, request.State, request.Country, request.ZipCode);
    }

    public static CardPaymentDetails ToCard(OrderEndpoints.CardRequest card)
    {
        if (card.BillingAddress == null)
        {
            throw new CheckoutException("Card billingAddress is required.");
        }

        return new CardPaymentDetails(
            card.Number,
            card.Expiry,
            card.SecurityCode,
            card.Name,
            new CardBillingAddress(
                card.BillingAddress.AddressLine1,
                card.BillingAddress.AddressLine2,
                card.BillingAddress.AdminArea1,
                card.BillingAddress.AdminArea2,
                card.BillingAddress.PostalCode,
                card.BillingAddress.CountryCode));
    }
}
