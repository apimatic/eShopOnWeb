using System.Security.Claims;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Maps between the JWT caller / ApplicationCore subscription models and the API DTOs.
/// </summary>
public static class SubscriptionMapping
{
    /// <summary>
    /// Builds the billing identity from the authenticated caller. The eShopOnWeb username
    /// (email) carried on the token is the stable, idempotent Maxio customer reference — it
    /// survives process restarts, unlike the in-memory identity store's generated user ids.
    /// </summary>
    public static SubscriberIdentity ToSubscriber(ClaimsPrincipal user)
    {
        var username = user.FindFirstValue(ClaimTypes.Name)
            ?? user.Identity?.Name
            ?? user.FindFirstValue(ClaimTypes.Email);

        if (string.IsNullOrWhiteSpace(username))
        {
            // [Authorize] should prevent this; guard anyway rather than create a blank customer.
            throw new MaxioIntegrationException("The authenticated caller has no identity claim.", 401);
        }

        var email = user.FindFirstValue(ClaimTypes.Email) ?? username;
        return new SubscriberIdentity(UserId: username, Email: email, FirstName: null, LastName: null);
    }

    public static SubscriptionPlanDto ToDto(this SubscriptionPlan plan) => new()
    {
        Id = plan.Id,
        Handle = plan.Handle,
        Name = plan.Name,
        Description = plan.Description,
        Price = plan.Price,
        Currency = plan.Currency,
        Interval = plan.Interval
    };

    public static SubscriptionDto ToDto(this CustomerSubscription subscription) => new()
    {
        Id = subscription.Id,
        State = subscription.State,
        PlanName = subscription.PlanName,
        PlanHandle = subscription.PlanHandle,
        Price = subscription.Price,
        Currency = subscription.Currency,
        Interval = subscription.Interval,
        CurrentPeriodStartsAt = subscription.CurrentPeriodStartsAt,
        CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
        NextBillingDate = subscription.NextBillingDate,
        CustomerId = subscription.CustomerId,
        CustomerReference = subscription.CustomerReference
    };
}
