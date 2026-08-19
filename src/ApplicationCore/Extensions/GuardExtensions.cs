using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.eShopWeb.ApplicationCore.Entities.BasketAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;

namespace Ardalis.GuardClauses;

public static class BasketGuards
{
    public static void EmptyBasketOnCheckout(this IGuardClause guardClause, IReadOnlyCollection<BasketItem> basketItems)
    {
        if (!basketItems.Any())
            throw new EmptyBasketOnCheckoutException();
    }

    /// <summary>
    /// Guards against a listing URL that is not an absolute http/https address.
    /// </summary>
    public static void InvalidListingUrl(this IGuardClause guardClause, string url, string parameterName)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new ArgumentException($"'{parameterName}' must be an absolute http or https URL.", parameterName);
        }
    }
}
