using System.Linq;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.OrderEndpoints;

namespace Microsoft.eShopWeb.PublicApi;

internal static class HttpContextExtensions
{
    public static string GetBuyerId(this HttpContext httpContext)
    {
        var name = httpContext.User.Identity?.Name
                   ?? httpContext.User.FindFirstValue(ClaimTypes.Name);
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new PaymentOperationException(401, "A signed-in shopper is required.");
        }

        return name;
    }

    public static CardPaymentDetails ToCardDetails(this CardDetailsRequest card)
    {
        return new CardPaymentDetails
        {
            Number = NormalizeCardNumber(card.Number),
            Expiry = card.Expiry,
            SecurityCode = card.SecurityCode,
            Name = card.Name,
            BillingAddress = card.BillingAddress is null
                ? null
                : new CardBillingAddress
                {
                    CountryCode = card.BillingAddress.CountryCode,
                    AddressLine1 = card.BillingAddress.AddressLine1,
                    AddressLine2 = card.BillingAddress.AddressLine2,
                    AdminArea2 = card.BillingAddress.AdminArea2,
                    AdminArea1 = card.BillingAddress.AdminArea1,
                    PostalCode = card.BillingAddress.PostalCode
                }
        };
    }

    private static string NormalizeCardNumber(string number)
        => new string((number ?? string.Empty).Where(char.IsDigit).ToArray());
}
