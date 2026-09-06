using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// The shopper already has a live subscription to a <em>different</em> plan. Signing them up again
/// would bill them twice, so the request is refused rather than silently duplicated. Switching plans
/// is a separate capability and is out of scope here.
/// </summary>
public class SubscriptionConflictException : Exception
{
    public SubscriptionConflictException(string message, string existingPlanHandle, int existingSubscriptionId)
        : base(message)
    {
        ExistingPlanHandle = existingPlanHandle;
        ExistingSubscriptionId = existingSubscriptionId;
    }

    public string ExistingPlanHandle { get; }
    public int ExistingSubscriptionId { get; }
}
