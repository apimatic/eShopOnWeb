using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>Mapping between the ApplicationCore billing domain and the PublicApi DTOs, plus the caller
/// identity resolution and the billing-failure → HTTP-response mapping used by all subscription endpoints.</summary>
public static class SubscriptionMappings
{
    public static SubscriptionPlanDto ToDto(SubscriptionPlan plan) => new()
    {
        Handle = plan.Handle,
        Name = plan.Name,
        Description = plan.Description,
        PriceInCents = plan.PriceInCents,
        Price = plan.PriceInCents / 100m,
        Currency = plan.Currency,
        IntervalUnit = plan.IntervalUnit,
        IntervalCount = plan.IntervalCount,
    };

    public static CustomerSubscriptionDto ToDto(CustomerSubscription subscription) => new()
    {
        Id = subscription.Id,
        PlanHandle = subscription.PlanHandle,
        PlanName = subscription.PlanName,
        PriceInCents = subscription.PriceInCents,
        Price = subscription.PriceInCents.HasValue ? subscription.PriceInCents.Value / 100m : null,
        State = subscription.State,
        NextBillingDate = subscription.NextBillingAt,
        CustomerId = subscription.CustomerId,
        CustomerReference = subscription.CustomerReference,
    };

    /// <summary>
    /// Builds the <see cref="SubscriberIdentity"/> from the caller's JWT. eShopOnWeb usernames are email
    /// addresses, so the username is used both as the app-owned billing reference and as the customer email.
    /// Returns false when the token carries no name claim (should not happen behind <c>[Authorize]</c>).
    /// </summary>
    public static bool TryCreateSubscriber(ClaimsPrincipal user, out SubscriberIdentity subscriber)
    {
        subscriber = null!;
        var name = user.FindFirst(ClaimTypes.Name)?.Value ?? user.Identity?.Name;
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        var username = name.Trim();
        var atIndex = username.IndexOf('@');
        var local = atIndex > 0 ? username.Substring(0, atIndex) : username;

        subscriber = new SubscriberIdentity
        {
            Reference = username,
            Email = username,
            FirstName = string.IsNullOrWhiteSpace(local) ? "eShop" : local,
            LastName = "Customer",
        };
        return true;
    }

    /// <summary>Maps a <see cref="MaxioBillingException"/> to a caller-facing HTTP result. The failure kind —
    /// not the raw provider status — decides the status, so an auth/quota failure on our credentials becomes a
    /// 502, never a 4xx blamed on the shopper.</summary>
    public static IResult ToProblemResult(MaxioBillingException exception)
    {
        var status = exception.Kind switch
        {
            MaxioBillingErrorKind.Validation => StatusCodes.Status422UnprocessableEntity,
            MaxioBillingErrorKind.NotFound => StatusCodes.Status404NotFound,
            MaxioBillingErrorKind.Upstream => StatusCodes.Status502BadGateway,
            _ => StatusCodes.Status500InternalServerError,
        };

        var body = new SubscriptionErrorResponse
        {
            Message = exception.Message,
            Errors = exception.ProviderMessages?.ToList() ?? new List<string>(),
        };

        return Results.Json(body, statusCode: status);
    }
}
