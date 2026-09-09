using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Text.Json.Serialization;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>Request body for <c>POST /api/subscriptions</c>.</summary>
public class SubscribeRequest
{
    /// <summary>The handle of the plan to subscribe to (from <c>GET /api/subscription-plans</c>).</summary>
    public string? PlanHandle { get; set; }
}

/// <summary>Response for <c>GET /api/subscription-plans</c>.</summary>
public class ListSubscriptionPlansResponse
{
    public List<SubscriptionPlan> Plans { get; set; } = new();
}

/// <summary>Response for <c>POST /api/subscriptions</c>.</summary>
public class SubscribeResponse
{
    public CustomerSubscription Subscription { get; set; } = default!;

    /// <summary>True when an existing live subscription was returned instead of creating a new one.</summary>
    public bool AlreadyExisted { get; set; }

    public string Message { get; set; } = string.Empty;
}

/// <summary>Response for <c>GET /api/my-subscriptions</c>.</summary>
public class ListMySubscriptionsResponse
{
    public List<CustomerSubscription> Subscriptions { get; set; } = new();
}

/// <summary>
/// Builds the Maxio-facing <see cref="SubscriberIdentity"/> from the JWT principal. The identity is
/// derived entirely from the authenticated token (the name claim), never from request input, so a
/// caller cannot subscribe as another user.
/// </summary>
public static class SubscriberIdentityFactory
{
    public static SubscriberIdentity? FromPrincipal(ClaimsPrincipal principal)
    {
        var userName = principal.FindFirstValue(ClaimTypes.Name) ?? principal.Identity?.Name;
        if (string.IsNullOrWhiteSpace(userName))
            return null;

        var atIndex = userName.IndexOf('@');
        var firstName = atIndex > 0 ? userName[..atIndex] : userName;

        // Reference and email are the stable per-user key (eShop usernames are email addresses).
        return new SubscriberIdentity(
            Reference: userName,
            Email: userName,
            FirstName: firstName,
            LastName: "eShopOnWeb");
    }
}
