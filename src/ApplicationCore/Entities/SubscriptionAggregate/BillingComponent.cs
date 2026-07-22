using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// A billable add-on defined on the provider's product family — for this integration, the
/// metered <c>api-call</c> component that pay-as-you-go usage accrues against.
/// </summary>
public class BillingComponent
{
    public BillingComponent(int id,
        string handle,
        string name,
        BillingComponentKind kind,
        decimal? unitPrice,
        string? productFamilyHandle)
    {
        Guard.Against.NullOrEmpty(handle, nameof(handle));

        Id = id;
        Handle = handle;
        Name = name;
        Kind = kind;
        UnitPrice = unitPrice;
        ProductFamilyHandle = productFamilyHandle;
    }

    public int Id { get; private set; }

    public string Handle { get; private set; }

    public string Name { get; private set; }

    public BillingComponentKind Kind { get; private set; }

    /// <summary>Price of a single unit in whole currency units (dollars), never cents.</summary>
    public decimal? UnitPrice { get; private set; }

    /// <summary>Handle of the product family the component is defined on, when the provider reports it.</summary>
    public string? ProductFamilyHandle { get; private set; }

    /// <summary>Only a metered component can accept usage reports (UC2).</summary>
    public bool IsMetered => Kind == BillingComponentKind.Metered;
}
