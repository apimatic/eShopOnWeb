using System.Security.Claims;
using Microsoft.eShopWeb.PublicApi.Maxio;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

internal static class SubscriptionMapper
{
    public static SubscriptionDto ToDto(MaxioSubscription subscription)
    {
        return new SubscriptionDto
        {
            Id = subscription.Id,
            State = subscription.State ?? string.Empty,
            Reference = subscription.Reference ?? string.Empty,
            PlanHandle = subscription.Product?.Handle ?? string.Empty,
            PlanName = subscription.Product?.Name ?? string.Empty,
            PriceInCents = subscription.ProductPriceInCents,
            // The next regularly scheduled charge tracks the end of the current period.
            NextBillingAt = subscription.CurrentPeriodEndsAt ?? subscription.NextAssessmentAt,
            ActivatedAt = subscription.ActivatedAt,
            CreatedAt = subscription.CreatedAt
        };
    }

    public static string GetUsername(ClaimsPrincipal user)
    {
        var username = user.FindFirst(ClaimTypes.Name)?.Value
            ?? user.FindFirst("unique_name")?.Value
            ?? user.FindFirst(ClaimTypes.Email)?.Value
            ?? user.Identity?.Name;

        if (string.IsNullOrWhiteSpace(username))
        {
            throw new System.InvalidOperationException("The authenticated token does not contain a username claim.");
        }

        return username;
    }
}
