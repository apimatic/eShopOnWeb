using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Shared resolution for "act on my own subscription, or (if Administrator) on any subscription"
/// - the access rule every UC2-UC4 PublicApi endpoint applies (plan.md: "Customer (own) or Admin (any)").
/// </summary>
internal static class SubscriptionAccess
{
    /// <summary>
    /// Resolves the subscription the caller may act on: the explicitly requested
    /// <paramref name="requestedSubscriptionId"/> when the caller is an Administrator or it belongs
    /// to them, or otherwise the caller's own active subscription. Returns null (with a Forbid/NotFound
    /// result set) when access is not allowed.
    /// </summary>
    public static async Task<(SubscriptionDetails? Subscription, IResult? Denied)> ResolveAsync(
        ISubscriptionService subscriptionService,
        string callerReference,
        bool callerIsAdmin,
        int? requestedSubscriptionId)
    {
        if (requestedSubscriptionId is int subscriptionId)
        {
            SubscriptionDetails subscription;
            try
            {
                subscription = await subscriptionService.GetSubscriptionAsync(subscriptionId);
            }
            catch (SubscriptionNotFoundException)
            {
                return (null, Results.NotFound());
            }

            if (!callerIsAdmin && subscription.CustomerReference != callerReference)
            {
                // A plain 403 status, not an auth challenge: Results.Forbid() would trigger the
                // configured challenge scheme's ForbidAsync, which for this JWT-secured JSON API
                // must never redirect to a login page.
                return (null, Results.Json(new { message = "You do not have access to this subscription." }, statusCode: StatusCodes.Status403Forbidden));
            }

            return (subscription, null);
        }

        var current = await subscriptionService.GetCurrentSubscriptionAsync(callerReference);
        if (current == null)
        {
            return (null, Results.NotFound("No subscription found for the current user."));
        }

        return (current, null);
    }
}
