using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Verifies that the configured usage component really resolves to a metered component before
/// any usage is reported (UC2 precondition). Implementations cache a successful validation so
/// the check costs one provider call per process, not one per usage report.
/// </summary>
public interface IMeteredComponentValidator
{
    /// <summary>
    /// Throws <see cref="Exceptions.BillingConfigurationException"/> when the handle does not
    /// resolve, or resolves to a component that is not metered.
    /// </summary>
    Task EnsureComponentIsMeteredAsync(IBillingClient billingClient,
        string componentHandle,
        CancellationToken cancellationToken = default);
}
