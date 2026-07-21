using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Microsoft.eShopWeb.Infrastructure.Services;

/// <summary>
/// Validates once, at host startup, that the configured pay-as-you-go component resolves to a
/// metered-kind component (UC2 precondition). Runs fire-and-forget and never throws out of
/// <see cref="StartAsync"/> — a Maxio outage or misconfiguration here must never prevent
/// eShopOnWeb itself from starting; it only means usage recording stays rejected until fixed.
/// </summary>
public class MaxioComponentValidationHostedService : IHostedService
{
    private readonly IServiceScopeFactory _scopeFactory;

    public MaxioComponentValidationHostedService(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _ = ValidateAsync(cancellationToken);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private async Task ValidateAsync(CancellationToken ct)
    {
        // IAppLogger<T> is registered Scoped, so it — like IBillingClient — must be resolved from
        // within this scope rather than injected into this Singleton hosted service's constructor.
        using var scope = _scopeFactory.CreateScope();
        var logger = scope.ServiceProvider.GetRequiredService<IAppLogger<MaxioComponentValidationHostedService>>();

        try
        {
            var billingClient = scope.ServiceProvider.GetRequiredService<IBillingClient>();
            var component = await billingClient.ValidateMeteredComponentAsync(ct);
            logger.LogInformation("Maxio metered component '{Handle}' validated at startup.", component.Handle);
        }
        catch (Exception ex)
        {
            logger.LogWarning("Maxio metered component startup validation failed; usage recording will be rejected until this is fixed: {Message}", ex.Message);
        }
    }
}
