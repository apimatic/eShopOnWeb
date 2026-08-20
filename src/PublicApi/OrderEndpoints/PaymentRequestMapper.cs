using System.Linq;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

internal static class PaymentRequestMapper
{
    public static string RequireBuyerId(HttpContext httpContext)
    {
        var buyerId = httpContext.User.Identity?.Name
            ?? httpContext.User.FindFirstValue(ClaimTypes.Name);
        if (string.IsNullOrWhiteSpace(buyerId))
        {
            throw new PaymentForbiddenException("The caller identity was missing from the token.");
        }

        return buyerId;
    }

    public static Address? ToAddress(AddressDto? dto)
    {
        if (dto is null)
        {
            return null;
        }

        return new Address(dto.Street, dto.City, dto.State, dto.Country, dto.ZipCode);
    }

    public static CardDetails ToCardDetails(CardPaymentRequest card)
    {
        if (string.IsNullOrWhiteSpace(card.Number) ||
            string.IsNullOrWhiteSpace(card.Expiry) ||
            string.IsNullOrWhiteSpace(card.SecurityCode) ||
            string.IsNullOrWhiteSpace(card.Name))
        {
            throw new PaymentValidationException("Card number, expiry, securityCode and name are required.");
        }

        var billing = card.BillingAddress ?? new CardBillingAddressRequest();
        if (string.IsNullOrWhiteSpace(billing.AddressLine1) ||
            string.IsNullOrWhiteSpace(billing.AdminArea2) ||
            string.IsNullOrWhiteSpace(billing.PostalCode) ||
            string.IsNullOrWhiteSpace(billing.CountryCode))
        {
            throw new PaymentValidationException("Card billingAddress requires addressLine1, adminArea2, postalCode and countryCode.");
        }

        return new CardDetails(
            card.Number,
            card.Expiry,
            card.SecurityCode,
            card.Name,
            new CardBillingAddress(
                billing.AddressLine1,
                billing.AddressLine2,
                billing.AdminArea2,
                billing.AdminArea1,
                billing.PostalCode,
                billing.CountryCode));
    }
}
