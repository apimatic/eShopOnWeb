using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Services;

/// <summary>
/// The Maxio entities the integration is configured against, resolved from their durable handles.
/// Maxio assigns numeric ids at creation and reassigns them on a re-seed, so the handles are the
/// identifiers the integration trusts and the ids are always looked up, never hard-coded.
/// </summary>
public sealed class MaxioCatalog
{
    public MaxioCatalog(int productFamilyId,
        string productFamilyHandle,
        IReadOnlyList<SubscriptionPlan> plans,
        MeteredComponentDefinition? meteredComponent)
    {
        ProductFamilyId = productFamilyId;
        ProductFamilyHandle = productFamilyHandle;
        Plans = plans;
        MeteredComponent = meteredComponent;
    }

    public int ProductFamilyId { get; }

    public string ProductFamilyHandle { get; }

    /// <summary>The live, non-archived plans in the family, in the order the provider returned them.</summary>
    public IReadOnlyList<SubscriptionPlan> Plans { get; }

    /// <summary>
    /// The configured pay-as-you-go component, or null when the handle does not resolve on the
    /// family. Null does not break plan browsing — only the UC2 usage paths reject it.
    /// </summary>
    public MeteredComponentDefinition? MeteredComponent { get; }

    public SubscriptionPlan? FindPlan(string handle)
    {
        foreach (var plan in Plans)
        {
            if (string.Equals(plan.Handle, handle, System.StringComparison.OrdinalIgnoreCase))
            {
                return plan;
            }
        }

        return null;
    }
}
