using System;
using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

public class CardDto
{
    public string Number { get; set; } = string.Empty;
    public int ExpirationMonth { get; set; }
    public int ExpirationYear { get; set; }
    public string SecurityCode { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public CardAddressDto? Address { get; set; }
}

public class CardAddressDto
{
    public string Line1 { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string Region { get; set; } = string.Empty;
    public string PostalCode { get; set; } = string.Empty;
    public string Country { get; set; } = string.Empty;
}

public static class PaymentApiMapper
{
    public static string BuyerId(ClaimsPrincipal user)
    {
        var buyerId = user.Identity?.Name;
        if (string.IsNullOrWhiteSpace(buyerId))
        {
            throw new PaymentOperationException("The caller identity is missing.", 401);
        }

        return buyerId;
    }

    public static CardInput ToCardInput(CardDto card)
    {
        return new CardInput(
            card.Number,
            card.ExpirationMonth,
            card.ExpirationYear,
            card.SecurityCode,
            card.Name,
            card.Address == null
                ? null
                : new CardAddressInput(
                    card.Address.Line1,
                    card.Address.City,
                    card.Address.Region,
                    card.Address.PostalCode,
                    card.Address.Country));
    }

    public static CardDto? ResolveCard(
        CardDto? nestedCard,
        string? number,
        int? expirationMonth,
        int? expirationYear,
        string? securityCode,
        string? name,
        CardAddressDto? address)
    {
        if (nestedCard != null)
        {
            return nestedCard;
        }

        if (string.IsNullOrWhiteSpace(number))
        {
            return null;
        }

        return new CardDto
        {
            Number = number,
            ExpirationMonth = expirationMonth ?? 0,
            ExpirationYear = expirationYear ?? 0,
            SecurityCode = securityCode ?? string.Empty,
            Name = name ?? string.Empty,
            Address = address
        };
    }

    public static int RouteId(RouteData routeData, string key)
    {
        if (routeData.Values.TryGetValue(key, out var value)
            && int.TryParse(value?.ToString(), out var id))
        {
            return id;
        }

        throw new PaymentOperationException($"Route value '{key}' is missing.", 400);
    }

    public static ObjectResult ErrorResult(Exception exception)
    {
        return exception switch
        {
            PaymentOperationException paymentException => new ObjectResult(new
            {
                error = paymentException.Message
            })
            {
                StatusCode = paymentException.StatusCode
            },
            FinixApiException finixException => new ObjectResult(new
            {
                error = finixException.Message,
                code = finixException.Code,
                logRef = finixException.LogRef
            })
            {
                StatusCode = 502
            },
            _ => new ObjectResult(new { error = exception.Message })
            {
                StatusCode = 500
            }
        };
    }
}
