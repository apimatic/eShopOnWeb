namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// A request to enroll a specific user in a specific plan. Handled idempotently: repeated
/// submissions for the same customer + plan must not create duplicate customers or subscriptions.
/// </summary>
public sealed class SubscribeCommand
{
    public SubscribeCommand(BillingCustomerIdentity customer, string planHandle)
    {
        Customer = customer;
        PlanHandle = planHandle;
    }

    public BillingCustomerIdentity Customer { get; }

    /// <summary>Handle of the target <see cref="SubscriptionPlan"/> to enroll in.</summary>
    public string PlanHandle { get; }
}
