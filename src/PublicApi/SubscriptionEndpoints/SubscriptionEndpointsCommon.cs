using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.eShopWeb.Infrastructure.Identity;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public record Subscriber(string UserId, string? Email);

public static class SubscriberResolver
{
    /// <summary>
    /// The JWT carries only the user name (ClaimTypes.Name); resolve it to the stable identity user id.
    /// Returns null when the token's user no longer exists.
    /// </summary>
    public static async Task<Subscriber?> ResolveAsync(ClaimsPrincipal principal, UserManager<ApplicationUser> userManager)
    {
        var name = principal.FindFirstValue(ClaimTypes.Name);
        if (string.IsNullOrWhiteSpace(name)) return null;
        var user = await userManager.FindByNameAsync(name);
        return user == null ? null : new Subscriber(user.Id, user.Email);
    }
}

public class SubscriptionPlansResponse : BaseResponse
{
    public SubscriptionPlansResponse(Guid correlationId) : base(correlationId) { }
    public SubscriptionPlansResponse() { }

    public System.Collections.Generic.IReadOnlyList<Subscriptions.PlanDto> Plans { get; set; } = Array.Empty<Subscriptions.PlanDto>();
    /// <summary>True when the plan list was cut short (page ceiling reached).</summary>
    public bool IsTruncated { get; set; }
}

public class SubscribeRequest : BaseRequest
{
    public string? PlanHandle { get; set; }
}

public class SubscribeResponse : BaseResponse
{
    public SubscribeResponse(Guid correlationId) : base(correlationId) { }
    public SubscribeResponse() { }

    public Subscriptions.SubscriptionDto? Subscription { get; set; }
    /// <summary>True when the user already held this plan and the existing subscription is returned.</summary>
    public bool AlreadySubscribed { get; set; }
}

public class MySubscriptionsResponse : BaseResponse
{
    public MySubscriptionsResponse(Guid correlationId) : base(correlationId) { }
    public MySubscriptionsResponse() { }

    public System.Collections.Generic.IReadOnlyList<Subscriptions.SubscriptionDto> Subscriptions { get; set; } = Array.Empty<Subscriptions.SubscriptionDto>();
    public int Count { get; set; }
}
