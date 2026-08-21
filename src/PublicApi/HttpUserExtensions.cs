using System.Linq;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.PublicApi.OrderEndpoints;

namespace Microsoft.eShopWeb.PublicApi;

internal static class HttpUserExtensions
{
    public static string RequireBuyerId(this ClaimsPrincipal user)
    {
        var buyerId = user.Identity?.Name;
        if (string.IsNullOrWhiteSpace(buyerId))
        {
            throw new CommerceException(StatusCodes.Status401Unauthorized, "A signed-in shopper is required.");
        }

        return buyerId;
    }

    public static Address ToAddress(this ShippingAddressRequest? shipTo)
        => new(
            street: string.IsNullOrWhiteSpace(shipTo?.Street) ? "123 Main St." : shipTo.Street,
            city: string.IsNullOrWhiteSpace(shipTo?.City) ? "Kent" : shipTo.City,
            state: string.IsNullOrWhiteSpace(shipTo?.State) ? "OH" : shipTo.State,
            country: string.IsNullOrWhiteSpace(shipTo?.Country) ? "United States" : shipTo.Country,
            zipcode: string.IsNullOrWhiteSpace(shipTo?.ZipCode) ? "44240" : shipTo.ZipCode);

    public static CardPaymentDetails ToCardDetails(this CardPaymentRequest card)
        => new()
        {
            Number = card.Number,
            Expiry = card.Expiry,
            SecurityCode = card.SecurityCode,
            Name = card.Name,
            BillingAddress = card.BillingAddress is null
                ? null
                : new CardBillingAddress
                {
                    AddressLine1 = card.BillingAddress.AddressLine1,
                    AddressLine2 = card.BillingAddress.AddressLine2,
                    AdminArea2 = card.BillingAddress.AdminArea2,
                    AdminArea1 = card.BillingAddress.AdminArea1,
                    PostalCode = card.BillingAddress.PostalCode,
                    CountryCode = string.IsNullOrWhiteSpace(card.BillingAddress.CountryCode)
                        ? "US"
                        : card.BillingAddress.CountryCode
                }
        };
}
