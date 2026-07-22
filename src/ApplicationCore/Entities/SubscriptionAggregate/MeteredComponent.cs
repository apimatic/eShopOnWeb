namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// A pay-as-you-go component available to subscriptions on a product family.
/// </summary>
public class MeteredComponent
{
    public MeteredComponent(int providerComponentId, string handle, string name, bool isMetered,
        string? pricingScheme, decimal? unitPrice)
    {
        ProviderComponentId = providerComponentId;
        Handle = handle;
        Name = name;
        IsMetered = isMetered;
        PricingScheme = pricingScheme;
        UnitPrice = unitPrice;
    }

    public int ProviderComponentId { get; }

    public string Handle { get; }

    public string Name { get; }

    /// <summary>
    /// True only when the provider reports this component as metered. UC2 refuses to record
    /// usage against a component that is not metered.
    /// </summary>
    public bool IsMetered { get; }

    public string? PricingScheme { get; }

    /// <summary>Price per unit in major units (dollars), when the pricing scheme exposes one.</summary>
    public decimal? UnitPrice { get; }
}
