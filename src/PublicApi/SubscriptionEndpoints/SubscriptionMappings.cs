using System.Security.Claims;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>Maps between the billing domain and the API's transport DTOs, and derives the subscriber
/// identity from the authenticated caller's token.</summary>
internal static class SubscriptionMappings
{
    public static SubscriptionPlanDto ToDto(this SubscriptionPlan plan) => new()
    {
        Handle = plan.Handle,
        Name = plan.Name,
        PriceInCents = plan.PriceInCents,
        FormattedPrice = plan.FormattedPrice,
        Interval = plan.Interval,
        ProductFamilyHandle = plan.ProductFamilyHandle,
        Description = plan.Description
    };

    public static CustomerSubscriptionDto ToDto(this CustomerSubscription subscription) => new()
    {
        Id = subscription.Id,
        State = subscription.State,
        PlanHandle = subscription.PlanHandle,
        PlanName = subscription.PlanName,
        PriceInCents = subscription.PriceInCents,
        FormattedPrice = subscription.FormattedPrice,
        NextBillingDate = subscription.NextBillingDate
    };

    /// <summary>
    /// Builds a <see cref="SubscriberIdentity"/> from the JWT-authenticated caller. The token's name claim
    /// (the eShopOnWeb username, an email) is the stable reference that makes customer provisioning
    /// idempotent. Never trusts client-supplied identity — the identity comes only from the token.
    /// </summary>
    public static SubscriberIdentity ToSubscriberIdentity(this ClaimsPrincipal user)
    {
        var name = user.Identity?.Name
            ?? user.FindFirst(ClaimTypes.Name)?.Value
            ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? user.FindFirst("sub")?.Value;

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new SubscriberIdentityException();
        }

        var email = user.FindFirst(ClaimTypes.Email)?.Value;
        if (string.IsNullOrWhiteSpace(email))
        {
            email = name; // eShopOnWeb usernames are email addresses
        }

        return new SubscriberIdentity(reference: name, email: email);
    }
}
