namespace Microsoft.eShopWeb.SubscriptionsSeed;

/// <summary>
/// The shape the billing sandbox must have for the subscription integration to work. Handles come
/// from configuration because they are what the running application resolves everything by; the
/// names, prices, and cadence are the fixed demo definition.
/// </summary>
internal sealed class SeedDefinition
{
    internal SeedDefinition(string familyHandle,
        string defaultPlanHandle,
        string alternatePlanHandle,
        string meteredComponentHandle)
    {
        FamilyHandle = familyHandle;
        DefaultPlanHandle = defaultPlanHandle;
        AlternatePlanHandle = alternatePlanHandle;
        MeteredComponentHandle = meteredComponentHandle;
    }

    internal const string FamilyName = "eShopSubscribe";
    internal const string FamilyDescription = "Recurring plans and metered add-ons for the eShopOnWeb storefront.";

    internal const string DefaultPlanName = "Pro Plan";
    internal const string DefaultPlanDescription = "The eShopOnWeb Pro subscription.";
    internal const decimal DefaultPlanPrice = 299.00m;

    internal const string AlternatePlanName = "Basic Plan";
    internal const string AlternatePlanDescription = "The eShopOnWeb Basic subscription.";
    internal const decimal AlternatePlanPrice = 29.00m;

    internal const int BillingInterval = 1;
    internal const string BillingIntervalUnit = "month";

    /// <summary>The demo plans deliberately do not require a payment method, so subscribing needs no card.</summary>
    internal const bool RequiresPaymentMethod = false;

    internal const string ComponentName = "API Calls";
    internal const string ComponentUnitName = "API call";
    internal const decimal ComponentUnitPrice = 0.01m;

    internal string FamilyHandle { get; }

    internal string DefaultPlanHandle { get; }

    internal string AlternatePlanHandle { get; }

    internal string MeteredComponentHandle { get; }
}
