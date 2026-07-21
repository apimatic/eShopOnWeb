using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Microsoft.eShopWeb.Infrastructure.Services;

/// <summary>
/// Best-effort startup check (UC2 precondition) that the configured metered usage component resolves
/// and is of metered kind. Runs fire-and-forget so a Maxio outage at boot never blocks the rest of
/// eShopOnWeb's storefront from starting - a failed check is simply retried lazily on the first real
/// usage call (see <see cref="MeteredComponentValidationCache"/>).
/// </summary>
public class MeteredComponentStartupValidator : IHostedService
{
    // IAppLogger<> is registered Scoped - resolved from a created scope below, alongside IBillingClient,
    // rather than injected directly (which would be invalid: a Singleton cannot consume a Scoped service).
    private readonly IServiceScopeFactory _scopeFactory;

    public MeteredComponentStartupValidator(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _ = ValidateBestEffortAsync(cancellationToken);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private async Task ValidateBestEffortAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var logger = scope.ServiceProvider.GetRequiredService<IAppLogger<MeteredComponentStartupValidator>>();

        try
        {
            var billingClient = scope.ServiceProvider.GetRequiredService<IBillingClient>();
            await billingClient.ValidateUsageComponentAsync(cancellationToken);
            logger.LogInformation("Maxio metered usage component validated successfully at startup.");
        }
        catch (Exception ex)
        {
            logger.LogWarning("Maxio metered usage component validation failed at startup (will retry before the first usage call): {0}", ex.Message);
        }
    }
}
