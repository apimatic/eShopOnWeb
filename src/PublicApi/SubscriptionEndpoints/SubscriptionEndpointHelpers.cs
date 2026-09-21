using System;
using System.Globalization;
using System.Linq;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionBilling;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Shared helpers for the subscription endpoints: deriving the caller identity from the JWT,
/// mapping domain DTOs to API DTOs, and translating <see cref="SubscriptionBillingException"/>
/// into a caller-facing result.
/// </summary>
internal static class SubscriptionEndpointHelpers
{
    /// <summary>
    /// Builds the billing identity from the authenticated principal. The caller's identity comes
    /// from the token (the name claim), never from the request body. Returns null when the
    /// principal carries no usable name.
    /// </summary>
    public static SubscriberIdentity? BuildSubscriber(ClaimsPrincipal? user)
    {
        var name = user?.Identity?.Name
            ?? user?.FindFirst(ClaimTypes.Name)?.Value
            ?? user?.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        var email = name.Contains('@') ? name : $"{name}@users.eshoponweb.local";
        var (firstName, lastName) = DeriveNames(email);
        return new SubscriberIdentity(name, email, firstName, lastName);
    }

    private static (string FirstName, string LastName) DeriveNames(string email)
    {
        var local = email.Split('@')[0];
        var parts = local.Split(new[] { '.', '_', '-', '+' }, StringSplitOptions.RemoveEmptyEntries);
        var first = Capitalize(parts.Length > 0 ? parts[0] : local);
        var last = parts.Length > 1 ? Capitalize(parts[^1]) : "Subscriber";
        return (first, last);
    }

    private static string Capitalize(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return "Subscriber";
        }
        return char.ToUpper(value[0], CultureInfo.InvariantCulture) + value.Substring(1);
    }

    public static SubscriptionPlanDto ToDto(SubscriptionPlanInfo plan) => new()
    {
        Handle = plan.Handle,
        Name = plan.Name,
        Description = plan.Description,
        PriceInCents = plan.PriceInCents,
        Price = plan.Price,
        Interval = plan.Interval,
        IntervalUnit = plan.IntervalUnit,
        ProductId = plan.ProductId
    };

    public static SubscriptionDto ToDto(SubscriptionInfo subscription) => new()
    {
        SubscriptionId = subscription.SubscriptionId,
        PlanHandle = subscription.PlanHandle,
        PlanName = subscription.PlanName,
        State = subscription.State,
        PriceInCents = subscription.PriceInCents,
        Price = subscription.Price,
        NextBillingDate = subscription.NextBillingDate,
        CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
        CreatedAt = subscription.CreatedAt
    };

    /// <summary>
    /// Maps a billing failure to a caller-facing HTTP result. Our-fault statuses (auth/quota) and
    /// unknown/transport failures become 502; a status the caller can act on (4xx) is passed
    /// through. Never leaks provider/SDK internals.
    /// </summary>
    public static IResult ToProblem(SubscriptionBillingException ex)
    {
        int status = ex.StatusCode switch
        {
            401 or 403 or 429 => StatusCodes.Status502BadGateway, // our credentials / our quota
            >= 400 and < 500 => ex.StatusCode.Value,              // caller can act on it
            _ => StatusCodes.Status502BadGateway                  // transport / 5xx / unknown
        };

        return Results.Problem(detail: ex.Message, statusCode: status, title: "Subscription billing error");
    }
}
