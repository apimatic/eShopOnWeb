using System;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// Intent to enrol an eShopOnWeb user in a plan. <see cref="UserName"/> is the identity the caller
/// proved with its token; it is never taken from the request body.
/// </summary>
public class SubscribeRequest
{
    public SubscribeRequest(string userName, string planHandle)
    {
        if (string.IsNullOrWhiteSpace(userName)) throw new ArgumentException("User name is required.", nameof(userName));
        if (string.IsNullOrWhiteSpace(planHandle)) throw new ArgumentException("Plan handle is required.", nameof(planHandle));

        UserName = userName;
        PlanHandle = planHandle;
    }

    public string UserName { get; }

    public string PlanHandle { get; }

    /// <summary>Contact e-mail recorded on the billing customer. Defaults to the user name when omitted.</summary>
    public string? Email { get; init; }

    public string? FirstName { get; init; }

    public string? LastName { get; init; }

    /// <summary>
    /// Optional caller-supplied key that scopes the idempotency of this request. When omitted, the
    /// (user, plan) pair is used, so a double-click still resolves to a single subscription.
    /// </summary>
    public string? IdempotencyKey { get; init; }
}
