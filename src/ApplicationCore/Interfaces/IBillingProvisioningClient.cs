using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Operator-only capabilities used to provision and verify the billing provider's catalogue (UC0).
/// </summary>
/// <remarks>
/// This surface is deliberately kept off <see cref="IBillingClient"/>: it is never reachable from
/// the storefront or the public API, and is used only by the one-shot seeding tool. It is
/// implemented by the same single class that owns all provider traffic.
/// </remarks>
public interface IBillingProvisioningClient
{
    /// <summary>Finds the product family with the given handle, or <c>null</c> when it does not exist.</summary>
    Task<BillingProductFamily?> FindProductFamilyByHandleAsync(string handle,
        CancellationToken cancellationToken = default);

    /// <summary>Creates a product family with a stable handle.</summary>
    Task<BillingProductFamily> CreateProductFamilyAsync(string handle,
        string name,
        string? description,
        CancellationToken cancellationToken = default);

    /// <summary>Lists the plans defined on a product family, archived ones included on request.</summary>
    Task<IReadOnlyCollection<BillingPlan>> ListPlansForFamilyAsync(BillingProductFamily family,
        bool includeArchived,
        CancellationToken cancellationToken = default);

    /// <summary>Creates a recurring plan inside a product family.</summary>
    Task<BillingPlan> CreatePlanAsync(BillingProductFamily family,
        string handle,
        string name,
        string description,
        decimal price,
        int interval,
        string intervalUnit,
        bool requiresPaymentMethod,
        CancellationToken cancellationToken = default);

    /// <summary>Archives a plan so a mis-created one can be replaced rather than mutated in place.</summary>
    Task<BillingPlan> ArchivePlanAsync(int planId, CancellationToken cancellationToken = default);

    /// <summary>Lists the components defined on a product family.</summary>
    Task<IReadOnlyCollection<BillingComponent>> ListComponentsForFamilyAsync(BillingProductFamily family,
        bool includeArchived,
        CancellationToken cancellationToken = default);

    /// <summary>Creates a metered, per-unit component on a product family.</summary>
    Task<BillingComponent> CreateMeteredComponentAsync(BillingProductFamily family,
        string handle,
        string name,
        string unitName,
        decimal unitPrice,
        CancellationToken cancellationToken = default);

    /// <summary>Archives a component so a mis-created one can be replaced rather than mutated in place.</summary>
    Task<BillingComponent> ArchiveComponentAsync(BillingProductFamily family,
        int componentId,
        CancellationToken cancellationToken = default);
}
