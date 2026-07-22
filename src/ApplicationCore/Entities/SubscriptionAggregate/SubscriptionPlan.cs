using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// A recurring plan a customer can subscribe to. Provider-agnostic projection of the billing
/// provider's product catalogue.
/// </summary>
public class SubscriptionPlan
{
    public SubscriptionPlan(int id,
        string handle,
        string name,
        string? description,
        long priceInCents,
        int interval,
        string intervalUnit,
        bool requiresPaymentMethod,
        bool isArchived,
        string? productFamilyHandle)
    {
        Guard.Against.NullOrEmpty(handle, nameof(handle));

        Id = id;
        Handle = handle;
        Name = name;
        Description = description;
        PriceInCents = priceInCents;
        Interval = interval;
        IntervalUnit = intervalUnit;
        RequiresPaymentMethod = requiresPaymentMethod;
        IsArchived = isArchived;
        ProductFamilyHandle = productFamilyHandle;
    }

    /// <summary>The provider-assigned numeric id. Not stable across a catalogue re-seed — prefer <see cref="Handle"/>.</summary>
    public int Id { get; }

    /// <summary>The durable identifier for this plan (e.g. <c>eshop-pro</c>).</summary>
    public string Handle { get; }

    public string Name { get; }
    public string? Description { get; }

    /// <summary>The recurring price in minor units (cents). The provider is the source of truth for this value.</summary>
    public long PriceInCents { get; }

    /// <summary>The recurring price in major units (dollars), derived from <see cref="PriceInCents"/>.</summary>
    public decimal Price => PriceInCents / 100m;

    /// <summary>How many <see cref="IntervalUnit"/>s make up one billing period (e.g. 1).</summary>
    public int Interval { get; }

    /// <summary>The billing period unit (e.g. <c>month</c>).</summary>
    public string IntervalUnit { get; }

    /// <summary>Whether the provider demands a stored payment method before enrolment.</summary>
    public bool RequiresPaymentMethod { get; }

    public bool IsArchived { get; }

    public string? ProductFamilyHandle { get; }
}
