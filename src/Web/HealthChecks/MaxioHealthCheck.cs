using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Microsoft.eShopWeb.Web.HealthChecks;

/// <summary>
/// Startup/liveness validation for the Maxio integration (UC0 verification, UC2 preconditions):
/// confirms the configured product family, products, and metered component resolve and that the
/// metered component is of metered kind. A failure here never blocks the app from starting or
/// serving unrelated requests — it only surfaces on the health endpoint.
/// </summary>
public class MaxioHealthCheck : IHealthCheck
{
    private readonly IBillingClient _billingClient;

    public MaxioHealthCheck(IBillingClient billingClient)
    {
        _billingClient = billingClient;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await _billingClient.EnsureConfigurationValidAsync(cancellationToken);
            return HealthCheckResult.Healthy("Maxio product family/products/metered component resolved as configured.");
        }
        catch (BillingProviderException ex)
        {
            return HealthCheckResult.Unhealthy("Maxio configuration validation failed. See plan.md UC0 to correct the sandbox seed.", ex);
        }
    }
}
