namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// The provider-agnostic description of the entities this integration is configured against
/// (the UC0 seed). Exposed by the billing client so ApplicationCore never has to know which
/// provider — or which provider settings class — supplied them.
/// </summary>
public class BillingCatalog
{
    public string ProductFamilyHandle { get; init; } = string.Empty;
    public string DefaultPlanHandle { get; init; } = string.Empty;
    public string AlternatePlanHandle { get; init; } = string.Empty;
    public string MeteredComponentHandle { get; init; } = string.Empty;
}
