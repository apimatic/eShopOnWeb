namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// A shopper's request to enroll on a plan.
/// </summary>
public class SubscribeRequest
{
    /// <summary>The eShopOnWeb user name (email) taken from the caller's bearer token.</summary>
    public string UserName { get; init; } = string.Empty;

    /// <summary>The handle of the plan to subscribe to.</summary>
    public string PlanHandle { get; init; } = string.Empty;

    /// <summary>Optional customer profile details; when omitted they are derived from <see cref="UserName"/>.</summary>
    public string? FirstName { get; init; }
    public string? LastName { get; init; }
    public string? Organization { get; init; }

    /// <summary>
    /// Optional caller-supplied key that makes a retried request safe: the same key produces the
    /// same uniqueness token at the billing system, so a replayed create is rejected rather than
    /// duplicated.
    /// </summary>
    public string? IdempotencyKey { get; init; }
}
