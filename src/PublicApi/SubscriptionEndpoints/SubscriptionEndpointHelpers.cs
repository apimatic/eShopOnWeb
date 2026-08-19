using System;
using System.Linq;
using System.Security.Claims;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.Infrastructure.Identity;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

internal static class SubscriptionEndpointHelpers
{
    public static string? GetUserName(ClaimsPrincipal user)
        => user.Identity?.Name
           ?? user.FindFirstValue(ClaimTypes.Name)
           ?? user.FindFirstValue(ClaimTypes.NameIdentifier);

    public static SubscribeToPlanRequest ToSubscribeRequest(ApplicationUser user, string productHandle)
    {
        var (firstName, lastName) = SplitDisplayName(user);
        return new SubscribeToPlanRequest
        {
            UserId = user.Id,
            Email = user.Email ?? user.UserName ?? $"{user.Id}@eshop.local",
            FirstName = firstName,
            LastName = lastName,
            ProductHandle = productHandle
        };
    }

    public static SubscriptionDto ToDto(CustomerSubscription subscription)
    {
        return new SubscriptionDto
        {
            Id = subscription.Id,
            State = subscription.State,
            ProductHandle = subscription.ProductHandle,
            ProductName = subscription.ProductName,
            Price = subscription.Price,
            NextBillingDate = subscription.NextBillingDate
        };
    }

    private static (string FirstName, string LastName) SplitDisplayName(ApplicationUser user)
    {
        var source = user.Email ?? user.UserName ?? "Customer";
        var local = source.Contains('@') ? source.Split('@')[0] : source;
        var parts = local.Split(new[] { '.', '_', '-', '+' }, StringSplitOptions.RemoveEmptyEntries);
        var first = parts.Length > 0 ? Capitalize(parts[0]) : "Customer";
        var last = parts.Length > 1 ? Capitalize(parts[1]) : "eShopOnWeb";
        return (first, last);
    }

    private static string Capitalize(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "Customer";
        }

        return char.ToUpperInvariant(value[0]) + value[1..].ToLowerInvariant();
    }
}
