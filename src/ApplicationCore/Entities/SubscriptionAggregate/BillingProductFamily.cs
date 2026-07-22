using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// The provider-side container that holds the recurring plans and the metered component.
/// </summary>
public class BillingProductFamily
{
    public BillingProductFamily(int id, string handle, string name)
    {
        Guard.Against.NullOrEmpty(handle, nameof(handle));

        Id = id;
        Handle = handle;
        Name = name;
    }

    public int Id { get; private set; }

    public string Handle { get; private set; }

    public string Name { get; private set; }
}
