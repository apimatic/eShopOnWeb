namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// The current period-to-date balance for the metered usage component on a subscription.
/// </summary>
public class BillingComponentBalance
{
    public BillingComponentBalance(int unitBalance)
    {
        UnitBalance = unitBalance;
    }

    public int UnitBalance { get; }
}
