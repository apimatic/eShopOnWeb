using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Identity;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Scoped helper shared by the subscription endpoints: the billing service plus resolution of the
/// caller (from the JWT) to the eShopOnWeb user the Maxio customer is keyed on.
/// </summary>
public class SubscriberContext
{
    private readonly UserManager<ApplicationUser> _userManager;

    public SubscriberContext(UserManager<ApplicationUser> userManager, ISubscriptionBillingService billing)
    {
        _userManager = userManager;
        Billing = billing;
    }

    public ISubscriptionBillingService Billing { get; }

    /// <summary>Returns null when the token's user no longer exists.</summary>
    public async Task<Subscriber?> ResolveAsync(ClaimsPrincipal principal)
    {
        var name = principal.Identity?.Name;
        if (string.IsNullOrEmpty(name)) return null;

        var user = await _userManager.FindByNameAsync(name);
        if (user is null) return null;

        return new Subscriber(user.Id, user.Email ?? user.UserName ?? name);
    }
}

internal static class SubscriptionMapping
{
    public static SubscriptionPlanDto ToDto(this SubscriptionPlan p) => new()
    {
        Handle = p.Handle,
        Name = p.Name,
        Description = p.Description,
        Price = p.Price,
        IntervalCount = p.IntervalCount,
        IntervalUnit = p.IntervalUnit,
    };

    public static SubscriptionDto ToDto(this CustomerSubscription s) => new()
    {
        SubscriptionId = s.SubscriptionId,
        PlanHandle = s.PlanHandle,
        PlanName = s.PlanName,
        Price = s.Price,
        Currency = s.Currency,
        State = s.State,
        NextBillingDate = s.NextBillingDate,
        CreatedAt = s.CreatedAt,
    };
}
