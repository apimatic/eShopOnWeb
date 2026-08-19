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
}

public static class UrlGuards
{
    /// <summary>
    /// Throws an <see cref="ArgumentException"/> if <paramref name="input"/> is not a well-formed
    /// absolute http/https URL; otherwise returns the trimmed value.
    /// </summary>
    public static string InvalidHttpUrl(this IGuardClause guardClause, string input, string parameterName)
    {
        var value = Guard.Against.NullOrWhiteSpace(input, parameterName).Trim();

        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new ArgumentException($"'{parameterName}' must be an absolute http or https URL.", parameterName);
        }

        return value;
    }
}
