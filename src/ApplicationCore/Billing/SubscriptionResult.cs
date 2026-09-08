namespace Microsoft.eShopWeb.ApplicationCore.Billing;

/// <summary>
/// Outcome of a subscribe attempt. Idempotent by design: re-subscribing to a plan the user is
/// already on returns the existing subscription with <see cref="Created"/> = false.
/// </summary>
public class SubscriptionResult
{
    public bool Created { get; init; }

    public Subscription Subscription { get; init; } = new Subscription();
}
