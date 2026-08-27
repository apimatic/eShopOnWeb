using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Microsoft.eShopWeb.PublicApi.Billing;

public sealed class MaxioHealthCheck(IMaxioBillingGateway gateway) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            await gateway.CheckHealthAsync(cancellationToken);
            return HealthCheckResult.Healthy("Maxio is reachable and configured for this environment.");
        }
        catch (BillingException ex)
        {
            return HealthCheckResult.Unhealthy(ex.Code);
        }
    }
}
