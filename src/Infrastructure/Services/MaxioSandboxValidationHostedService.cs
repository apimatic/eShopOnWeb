using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Microsoft.eShopWeb.Infrastructure.Services;

/// <summary>
/// Best-effort startup validation (UC0/UC2 preconditions): confirms the configured product family,
/// plans, and metered component handles resolve on the Maxio sandbox. Never throws - a Maxio outage
/// or misconfiguration must never block eShopOnWeb from starting; it only logs a warning to act on.
/// </summary>
public sealed class MaxioSandboxValidationHostedService : IHostedService
{
    private readonly IServiceScopeFactory _scopeFactory;

    public MaxioSandboxValidationHostedService(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        // IBillingClient and IAppLogger<> are both scoped - resolve everything through one scope
        // rather than injecting them into this singleton's constructor.
        using var scope = _scopeFactory.CreateScope();
        var billingClient = scope.ServiceProvider.GetRequiredService<IBillingClient>();
        var logger = scope.ServiceProvider.GetRequiredService<IAppLogger<MaxioSandboxValidationHostedService>>();

        try
        {
            await billingClient.ValidateConfigurationAsync(cancellationToken);
            logger.LogInformation(
                "Maxio sandbox configuration validated: product family, plans, and metered component all resolve to their configured handles.");
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                "Maxio sandbox configuration validation failed - subscription features may reject requests until this is fixed: {0}", ex.Message);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
