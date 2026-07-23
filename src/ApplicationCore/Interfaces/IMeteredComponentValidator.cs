using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Guards the UC2 precondition that the configured component handle resolves to a component of
/// metered kind on the product family. Until that has been confirmed once, no usage may be recorded.
/// </summary>
public interface IMeteredComponentValidator
{
    /// <summary>
    /// Returns the validated metered component, resolving it from the provider on first use and
    /// caching the confirmed result afterwards.
    /// </summary>
    /// <exception cref="Exceptions.BillingConfigurationException">
    /// The handle does not resolve, or resolves to a component that is not metered. The remedy is to
    /// correct the seed (UC0), so the failure is not cached and a later call retries the lookup.
    /// </exception>
    Task<MeteredComponent> GetValidatedComponentAsync(CancellationToken cancellationToken = default);
}
